using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Collections.Generic;
using System.Reflection;
using TAS.EverestInterop;
using TAS.StudioCommunication;

namespace TAS {
	[Flags]
	public enum State {
		None = 0,
		Enable = 1,
		Record = 2,
		FrameStep = 4,
		Disable = 8,
		Delay = 16
	}
	public static partial class Manager {
		static Manager() {
			FieldInfo strawberryCollectTimer = typeof(Strawberry).GetField("collectTimer", BindingFlags.Instance | BindingFlags.NonPublic);
			FieldInfo dashCooldownTimer = typeof(Player).GetField("dashCooldownTimer", BindingFlags.Instance | BindingFlags.NonPublic);
			FieldInfo jumpGraceTimer = typeof(Player).GetField("jumpGraceTimer", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodInfo WallJumpCheck = typeof(Player).GetMethod("WallJumpCheck", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodInfo UpdateVirtualInputs = typeof(MInput).GetMethod("UpdateVirtualInputs", BindingFlags.Static | BindingFlags.NonPublic);

			Manager.UpdateVirtualInputs = (d_UpdateVirtualInputs)UpdateVirtualInputs.CreateDelegate(typeof(d_UpdateVirtualInputs));
			Manager.WallJumpCheck = (d_WallJumpCheck)WallJumpCheck.CreateDelegate(typeof(d_WallJumpCheck));
			StrawberryCollectTimer = strawberryCollectTimer.CreateDelegate_Get<GetBerryFloat>();
			DashCooldownTimer = dashCooldownTimer.CreateDelegate_Get<GetFloat>();
			JumpGraceTimer = jumpGraceTimer.CreateDelegate_Get<GetFloat>();
		}
		
		private static FieldInfo strawberryCollectTimer = typeof(Strawberry).GetField("collectTimer", BindingFlags.Instance | BindingFlags.NonPublic);

		//The things we do for faster replay times
		private delegate void d_UpdateVirtualInputs();
		private static d_UpdateVirtualInputs UpdateVirtualInputs;
		private delegate bool d_WallJumpCheck(Player player, int dir);
		private static d_WallJumpCheck WallJumpCheck;
		private delegate float GetBerryFloat(Strawberry berry);
		private static GetBerryFloat StrawberryCollectTimer;
		private delegate float GetFloat(Player player);
		private static GetFloat DashCooldownTimer;
		private static GetFloat JumpGraceTimer;
		
		public static bool Running, Recording;
		public static InputController controller = new InputController("Celeste.tas");
		public static State lastState, state, nextState;
		public static string CurrentStatus, PlayerStatus = "";
		public static int FrameStepCooldown, FrameLoops = 1;
		public static bool enforceLegal, allowUnsafeInput;
		public static int forceDelayTimer = 0;
		public static bool forceDelay;
		private static Vector2 lastPos;
		private static long lastTimer;
		private static List<VirtualButton.Node>[] playerBindings;
		public static Buttons grabButton = Buttons.Back;
		public static AnalogueMode analogueMode = AnalogueMode.Ignore;//Circle; //Needs to be tested with the libTAS converter
		public static CelesteTASModuleSettings settings => CelesteTASModule.Settings;
		public static bool kbTextInput;
		private static bool ShouldForceState => HasFlag(nextState, State.FrameStep) && !Hotkeys.hotkeyFastForward.overridePressed;

		public static void UpdateInputs() {
			lastState = state;
			Hotkeys.Update();
			Savestates.HandleSaveStates();
			Savestates.routine?.Update();
			HandleFrameRates();
			CheckToEnable();
			FrameStepping();

			if (HasFlag(state, State.Enable)) {
				Running = true;

				if (HasFlag(state, State.FrameStep)) {
					StudioCommunicationClient.instance?.SendStateAndPlayerData(CurrentStatus, PlayerStatus, !ShouldForceState);
					return;
				}
				/*
				if (HasFlag(state, State.Record)) {
					controller.RecordPlayer();
				}
				*/
				else {
					bool fastForward = controller.HasFastForward;
					controller.AdvanceFrame(false);
					if (fastForward
						&& (!controller.HasFastForward
						|| controller.Current.ForceBreak
						&& controller.CurrentInputFrame == controller.Current.Frames)) {
						nextState |= State.FrameStep;
						FrameLoops = 1;
					}
					if (!controller.CanPlayback || (!allowUnsafeInput && !(Engine.Scene is Level || Engine.Scene is LevelLoader || Engine.Scene is LevelExit || controller.CurrentFrame <= 1)))
						DisableRun();
				}
				string status = controller.Current.Line + "[" + controller.ToString() + "]";
				CurrentStatus = status;
			}/*
			else if (HasFlag(state, State.Delay)) {
				Level level = Engine.Scene as Level;
				if (level.CanPause && Engine.FreezeTimer == 0f)
					EnableRun();
				
			}*/
			else {
				Running = false;
				CurrentStatus = null;
				if (!Engine.Instance.IsActive) {
					UpdateVirtualInputs();
					for (int i = 0; i < 4; i++) {
						if (MInput.GamePads[i].Attached) {
							MInput.GamePads[i].CurrentState = GamePad.GetState((PlayerIndex)i);
						}
					}
				}
			}
			StudioCommunicationClient.instance?.SendStateAndPlayerData(CurrentStatus, PlayerStatus, !ShouldForceState);
		}

		public static bool IsLoading() {
			if (Engine.Scene is Level level) {
				if (!level.IsAutoSaving())
					return false;
				return level.Session.Level == "end-cinematic";
			}
			if (Engine.Scene is SummitVignette summit)
				return !(bool)summit.GetPrivateFieldValue("ready");
			else if (Engine.Scene is Overworld overworld)
				return overworld.Current is OuiFileSelect slot && slot.SlotIndex >= 0 && slot.Slots[slot.SlotIndex].StartingGame;
			bool isLoading = (Engine.Scene is LevelExit) || (Engine.Scene is LevelLoader) || (Engine.Scene is GameLoader) || Engine.Scene.GetType().Name == "LevelExitToLobby";
			return isLoading;
		}

		public static float GetAngle(Vector2 vector) {
			float angle = 360f / 6.283186f * Calc.Angle(vector);
			if (angle < -90.01f)
				return 450f + angle;
			else
				return 90f + angle;
		}

		private static void HandleFrameRates() {
			if (HasFlag(state, State.Enable) && !HasFlag(state, State.FrameStep) && !HasFlag(nextState, State.FrameStep) && !HasFlag(state, State.Record)) {
				if (controller.HasFastForward) {
					FrameLoops = controller.FastForwardSpeed;
					return;
				}
				//q: but euni, why not just use the hotkey system you implemented?
				//a: i have no fucking idea
				if (Hotkeys.IsKeyDown(settings.KeyFastForward.Keys) || Hotkeys.hotkeyFastForward.overridePressed) {
					FrameLoops = 10;
					return;
				}
			}
			FrameLoops = 1;
		}
		
		private static void FrameStepping() {
			bool frameAdvance = Hotkeys.hotkeyFrameAdvance.pressed && !Hotkeys.hotkeyStart.pressed;
			bool pause = Hotkeys.hotkeyPause.pressed && !Hotkeys.hotkeyStart.pressed;
			
			if (HasFlag(state, State.Enable) && !HasFlag(state, State.Record)) {
				if (HasFlag(nextState, State.FrameStep)) {
					state |= State.FrameStep;
					nextState &= ~State.FrameStep;
				}

				if (frameAdvance && !Hotkeys.hotkeyFrameAdvance.wasPressed) {
					if (!HasFlag(state, State.FrameStep)) {
						state |= State.FrameStep;
						nextState &= ~State.FrameStep;
					}
					else {
						state &= ~State.FrameStep;
						nextState |= State.FrameStep;
						controller.AdvanceFrame(true);
					}
					FrameStepCooldown = 60;
				}
				else if (pause && !Hotkeys.hotkeyPause.wasPressed) {
					state &= ~State.FrameStep;
					nextState &= ~State.FrameStep;

				}
				else if (HasFlag(lastState, State.FrameStep) && HasFlag(state, State.FrameStep) && Hotkeys.hotkeyFastForward.pressed) {
					state &= ~State.FrameStep;
					nextState |= State.FrameStep;
					controller.AdvanceFrame(true);
				}
			}
		}
		
		private static void CheckToEnable() {
			if (Hotkeys.hotkeyStart.pressed) {
				if (!HasFlag(state, State.Enable))
					nextState |= State.Enable;
				else
					nextState |= State.Disable;
			}
			else if (HasFlag(nextState, State.Enable)) {
				if (Engine.Scene is Level level && (!level.CanPause || Engine.FreezeTimer > 0)) {
					
					controller.InitializePlayback();
					if (controller.Current.HasActions(Actions.Restart) || controller.Current.HasActions(Actions.Start)) {
						
						nextState |= State.Delay;
						FrameLoops = 400;
						return;
					}
					
				}
				EnableRun();
			}
			else if (HasFlag(nextState, State.Disable))
				DisableRun();
		}
		private static void DisableRun() {
			Running = false;
			/*
			if (Recording) {
				controller.WriteInputs();
			}
			*/
			Recording = false;
			state = State.None;
			nextState = State.None;
			RestorePlayerBindings();
			Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput = kbTextInput;
			controller.resetSpawn = null;
			if (ExportSyncData) {
				EndExport();
				ExportSyncData = false;
			}
			enforceLegal = false;
			allowUnsafeInput = false;
			analogueMode = AnalogueMode.Ignore;//Circle;
		}

		private static void EnableRun() {
			nextState &= ~State.Enable;
			UpdateVariables(false);
			BackupPlayerBindings();
			kbTextInput = Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput;
			Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput = false;
		}

		public static void EnableExternal() => EnableRun();

		public static void DisableExternal() => DisableRun();

		private static void BackupPlayerBindings() {
			playerBindings = new List<VirtualButton.Node>[5] { Input.Jump.Nodes, Input.Dash.Nodes, Input.Grab.Nodes, Input.Talk.Nodes, Input.QuickRestart.Nodes};
			Input.Jump.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(Input.Gamepad, Buttons.A), new VirtualButton.PadButton(Input.Gamepad, Buttons.Y) };
			Input.Dash.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(Input.Gamepad, Buttons.B), new VirtualButton.PadButton(Input.Gamepad, Buttons.X) };
			Input.Grab.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(Input.Gamepad, grabButton) };
			Input.Talk.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(Input.Gamepad, Buttons.B) };
			Input.QuickRestart.Nodes = new List<VirtualButton.Node> { new VirtualButton.PadButton(Input.Gamepad, Buttons.LeftShoulder) };
		}

		private static void RestorePlayerBindings() {
			//This can happen if DisableExternal is called before any TAS has been run
			if (playerBindings == null)
				return;
			Input.Jump.Nodes = playerBindings[0];
			Input.Dash.Nodes = playerBindings[1];
			Input.Grab.Nodes = playerBindings[2];
			Input.Talk.Nodes = playerBindings[3];
			Input.QuickRestart.Nodes = playerBindings[4];
		}

		private static void UpdateVariables(bool recording) {
			state |= State.Enable;
			state &= ~State.FrameStep;
			if (recording) {
				Recording = recording;
				state |= State.Record;
				controller.InitializeRecording();
			} else {
				state &= ~State.Record;
				controller.InitializePlayback();
			}
			Running = true;
		}

		private static bool HasFlag(State state, State flag) =>
			(state & flag) == flag;

		public static void SetInputs(InputRecord input) {
			GamePadDPad pad = default;
			GamePadThumbSticks sticks = default;
			GamePadState state = default;

			if (input.HasActions(Actions.Feather))
				SetFeather(input, ref pad, ref sticks);
			else
				SetDPad(input, ref pad, ref sticks);

			SetState(input, ref state, ref pad, ref sticks);

			bool found = false;
			for (int i = 0; i < 4; i++) {
				MInput.GamePads[i].Update();
				if (MInput.GamePads[i].Attached) {
					found = true;
					MInput.GamePads[i].CurrentState = state;
				}
			}

			if (!found) {
				MInput.GamePads[0].CurrentState = state;
				MInput.GamePads[0].Attached = true;
			}

			if (input.HasActions(Actions.Confirm)) {
				MInput.Keyboard.CurrentState = new KeyboardState(Keys.Enter);
			} else {
				MInput.Keyboard.CurrentState = new KeyboardState();
			}

			UpdateVirtualInputs();
		}

		private static void SetFeather(InputRecord input, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
			pad = new GamePadDPad(ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
			Vector2 aim = ValidateFeatherInput(input);
			sticks = new GamePadThumbSticks(aim, new Vector2(0, 0));
		}

		private static void SetDPad(InputRecord input, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
			pad = new GamePadDPad(
				input.HasActions(Actions.Up) ? ButtonState.Pressed : ButtonState.Released,
				input.HasActions(Actions.Down) ? ButtonState.Pressed : ButtonState.Released,
				input.HasActions(Actions.Left) ? ButtonState.Pressed : ButtonState.Released,
				input.HasActions(Actions.Right) ? ButtonState.Pressed : ButtonState.Released
			);
			sticks = new GamePadThumbSticks(new Vector2(0, 0), new Vector2(0, 0));
		}

		private static void SetState(InputRecord input, ref GamePadState state, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
			state = new GamePadState(
				sticks,
				new GamePadTriggers(input.HasActions(Actions.Journal) ? 1f : 0f, 0),
				new GamePadButtons(
					(input.HasActions(Actions.Jump) ? Buttons.A : 0)
					| (input.HasActions(Actions.Jump2) ? Buttons.Y : 0)
					| (input.HasActions(Actions.Dash) ? Buttons.B : 0)
					| (input.HasActions(Actions.Dash2) ? Buttons.X : 0)
					| (input.HasActions(Actions.Grab) ? grabButton : 0)
					| (input.HasActions(Actions.Start) ? Buttons.Start : 0)
					| (input.HasActions(Actions.Restart) ? Buttons.LeftShoulder : 0)
				),
				pad
			);
		}

		public enum AnalogueMode {
			Ignore,
			Circle,
			Square,
			Precise,
		}

		private static Vector2 ValidateFeatherInput(InputRecord input) {
			const float maxShort = short.MaxValue;
			short X;
			short Y;
			switch (analogueMode) {
				case AnalogueMode.Ignore:
					return new Vector2(input.GetX(), input.GetY());
				case AnalogueMode.Circle:
					X = (short)(input.GetX() * maxShort);
					Y = (short)(input.GetY() * maxShort);
					break;
				case AnalogueMode.Square:
					float x = input.GetX();
					float y = input.GetY();
					float mult = 1 / Math.Max(Math.Abs(x), Math.Abs(y));
					x *= mult;
					y *= mult;
					X = (short)(x * maxShort);
					Y = (short)(y * maxShort);
					break;
				case AnalogueMode.Precise:
					if (input.Angle == 0) {
						X = 0;
						Y = short.MaxValue;
						break;
					}
					GetPreciseFeatherPos(input.GetX(), input.GetY(), out X, out Y);
					break;
				default:
					throw new Exception("what the fuck");
			}
			// SDL2_FNAPlatform.GetGamePadState()
			// (float)SDL.SDL_GameControllerGetAxis(intPtr, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX) / 32767f

			return new Vector2((float)X / maxShort, (float)Y / maxShort);

		}

		//https://www.ics.uci.edu/~eppstein/numth/frap.c
		private static void GetPreciseFeatherPos(float xPos, float yPos, out short outX, out short outY) {

			//special cases where this is imprecise
			if (Math.Abs(xPos) == Math.Abs(yPos) || Math.Abs(xPos) < 1E-10 || Math.Abs(yPos) < 1E-10) {
				if (Math.Abs(xPos) < 1E-10) xPos = 0;
				if (Math.Abs(yPos) < 1E-10) yPos = 0;
				outX = (short)(short.MaxValue * (short)Math.Sign(xPos));
				outY = (short)(short.MaxValue * (short)Math.Sign(yPos));
				return;
			}

			if (Math.Abs(xPos) > Math.Abs(yPos)) {
				GetPreciseFeatherPos(yPos, xPos, out outY, out outX);
				return;
			}


			long[][] m = new long[2][];
			m[0] = new long[2];
			m[1] = new long[2];
			double x = xPos / yPos;
			double startx = x;
			short maxden = short.MaxValue;
			long ai;

			/* initialize matrix */
			m[0][0] = m[1][1] = 1;
			m[0][1] = m[1][0] = 0;

			/* loop finding terms until denom gets too big */
			while (m[1][0] * (ai = (long)x) + m[1][1] <= maxden) {
				long t;
				t = m[0][0] * ai + m[0][1];
				m[0][1] = m[0][0];
				m[0][0] = t;
				t = m[1][0] * ai + m[1][1];
				m[1][1] = m[1][0];
				m[1][0] = t;
				if (x == (double)ai)
					break;     // AF: division by zero
				x = 1 / (x - (double)ai);
				if (x > (double)0x7FFFFFFF)
					break;  // AF: representation failure
			}

			/* now remaining x is between 0 and 1/ai */
			/* approx as either 0 or 1/m where m is max that will fit in maxden */
			/* first try zero */
			outX = (short)m[0][0];
			outY = (short)m[1][0];

			double err1 = startx - ((double)m[0][0] / (double)m[1][0]);

			/* now try other possibility */
			ai = (maxden - m[1][1]) / m[1][0];
			m[0][0] = m[0][0] * ai + m[0][1];
			m[1][0] = m[1][0] * ai + m[1][1];

			double err2 = startx - ((double)m[0][0] / (double)m[1][0]);


			//magic
			if (err1 > err2) {
				outX = (short)m[0][0];
				outY = (short)m[1][0];
			}

			//why is there no short negation operator lmfao
			if (yPos < 0) {
				outX = (short)-outX;
				outY = (short)-outY;
			}

			//make sure it doesn't end up in the deadzone
			short mult = (short)Math.Floor(short.MaxValue / (float)Math.Max(Math.Abs(outX), Math.Abs(outY)));
			outX *= mult;
			outY *= mult;

			return;
		}

	}
}
