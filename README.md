# NineSolsTAS

## TAS tools for Nine Sols based on CelesteTAS

## Documentation

You can find documentation around CelesteTAS and Celeste Studio, as well as general TASing references on the [wiki](https://github.com/EverestAPI/CelesteTAS-EverestInterop/wiki).  
If you want to contribute to tooling documentation or TASing references, feel free to edit the wiki!

## Input File
The input file is a text file with `tas` as a suffix, e.g. `1A.tas`.

Format for the input file is (Frames),(Actions)

e.g. `123,R,J` (For `123` frames, hold `Right` and `Jump`)

## Available Actions
- `R` = Right
- `L` = Left
- `U` = Up
- `D` = Down
- `J` = Jump 
- `X` = Dash
- `E` = Interact
- `A` = Attack
- `S` = Shoot
- `K` = Parry
- `T` = Talisman
- `N` = Nymph
- `H` = Heal
- `P` = Custom Button Press Modifier (used to press inputs added by mods after binding them using the [Set command](Docs/Commands.md#set), e.g. `15,R,X,PA` after binding A to a custom input)

## Controls
While in game or in Studio:
- Start/Stop Playback: `RightControl`
- Restart Playback: `Equals`
- Fast Forward / Frame Advance Continuously: `RightShift` or `Controller Right Analog Stick`
- Fast Forward to Next Comment: `RightAlt + RightShift`
- Slow Forward: `\`
- Pause / Frame Advance: `[`
- Pause / Resume: `]`
- Toggle Hitboxes: `LeftControl + B`
- Toggle Simplified Graphics: `LeftControl + N`
- Toggle Center Camera: `LeftControl + M`
- Save State: `RightAlt + Minus`
- Clear State: `RightAlt + Back`
- Info HUD:
  * While holding the Info HUD hotkey, left-click to move the HUD around
  * Double press the Info HUD hotkey to toggle it
  * While Holding the Info HUD hotkey, left-click on entity to watch the entity
- These can be rebound in Mod Options
  * You will have to rebind some of these if you are on a non-US keyboard layout.
  * Binding multiple keys to a control will cause those keys to act as a key-combo.

## Special Input

### Breakpoints
- You can create a breakpoint in the input file by typing `***` by itself on a single line
- The TAS, when played back from the start will fast-forward until it reaches that line and will then pause the TAS
- `***S` will make a [savestate](#savestate), which can reduce TAS playback time. 
- You can specify the speed with `***X`, where `X` is the speedup factor. e.g. `***10` will go at 10x speed, `***0.5` will go at 0.5x speed.

### Commands
- Various commands exist to facilitate TAS playback. [Documentation can be found here](https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/master/Docs/Commands.md).