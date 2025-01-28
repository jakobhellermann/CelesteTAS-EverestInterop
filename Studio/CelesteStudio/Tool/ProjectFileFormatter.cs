using CelesteStudio.Communication;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CelesteStudio.Tool;

public class ProjectFileFormatterDialog : Eto.Forms.Dialog {
    private const string Version = "1.0.0";

    private readonly Button projectRootButton;
    private string projectRoot;

    private readonly CheckBox editRoomIndices;
    private readonly CheckBox editCommands;

    private readonly NumericStepper startingIndex;
    private readonly DropDown roomIndexType;

    private readonly CheckBox forceCorrectCasing;
    private readonly TextBox argumentSeparator;

    private ProjectFileFormatterDialog() {
        Title = $"Project File Formatter v{Version}";
        Icon = Assets.AppIcon;

        Menu = new MenuBar {
            AboutItem = MenuUtils.CreateAction("About...", Keys.None, () => {
                Studio.ShowAboutDialog(new AboutDialog {
                    ProgramName = "Project File Formatter",
                    ProgramDescription = "Applies various formatting choices to all files in the specified project",
                    Version = Version,

                    Developers = ["psyGamer"],
                    Logo = Icon,
                }, this);
            }),
        };

        const int rowWidth = 200;

        // General config
        projectRoot = FileRefactor.FindProjectRoot(Studio.Instance.Editor.Document.FilePath);
        string projectConfigPath = Path.Combine(projectRoot, StyleConfig.ConfigFile);

        var currentConfig = StyleConfig.Load(projectConfigPath);

        // Only allow changing if a config for the current project isn't present
        projectRootButton = new Button { Text = projectRoot, Width = 200, Enabled = !File.Exists(projectConfigPath) };
        projectRootButton.Click += (_, _) => {
            var dialog = new SelectFolderDialog {
                Title = "Select project root folder",
                Directory = projectRoot
            };

            if (dialog.ShowDialog(this) == DialogResult.Ok) {
                projectRoot = dialog.Directory;
                projectRootButton.Text = projectRoot;

                currentConfig = StyleConfig.Load(Path.Combine(projectRoot, StyleConfig.ConfigFile));
            }
        };

        // Auto-room-indexing
        editRoomIndices = new CheckBox { Width = rowWidth, Checked = true };
        startingIndex = new NumericStepper { MinValue = 0, DecimalPlaces = 0, Width = rowWidth, Value = currentConfig.RoomLabelStartingIndex ?? 0, Enabled = currentConfig.RoomLabelStartingIndex == null };
        roomIndexType = new DropDown {
            Items = {
                new ListItem { Text = "Only current File", Key = nameof(AutoRoomIndexing.CurrentFile) },
                new ListItem { Text = "Including Read-commands", Key = nameof(AutoRoomIndexing.IncludeReads) },
            },
            SelectedKey = currentConfig.RoomLabelIndexing?.ToString() ?? nameof(AutoRoomIndexing.CurrentFile),
            Width = rowWidth,
            Enabled = currentConfig.RoomLabelIndexing == null,
        };

        // Command formatting
        editCommands = new CheckBox { Width = rowWidth, Checked = true };
        forceCorrectCasing = new CheckBox { Width = rowWidth, Checked = true };
        argumentSeparator = new TextBox { Width = rowWidth, Text = currentConfig.CommandArgumentSeparator ?? ", ", Enabled = currentConfig.CommandArgumentSeparator == null };

        var autoRoomIndexingLayout = new DynamicLayout { DefaultSpacing = new Size(10, 10) };
        {
            autoRoomIndexingLayout.BeginVertical();
            autoRoomIndexingLayout.BeginHorizontal();

            autoRoomIndexingLayout.AddCentered(new Label { Text = "Starting Index" });
            autoRoomIndexingLayout.Add(startingIndex);

            autoRoomIndexingLayout.EndBeginHorizontal();

            autoRoomIndexingLayout.AddCentered(new Label { Text = "Room Indexing Type" });
            autoRoomIndexingLayout.Add(roomIndexType);
        }
        var commandLayout = new DynamicLayout { DefaultSpacing = new Size(10, 10) };
        {
            commandLayout.BeginVertical();
            commandLayout.BeginHorizontal();

            commandLayout.AddCentered(new Label { Text = "Force Correct Casing" });
            commandLayout.Add(forceCorrectCasing);

            commandLayout.EndBeginHorizontal();

            commandLayout.AddCentered(new Label { Text = "Argument Separator" });
            commandLayout.Add(argumentSeparator);
        }

        DefaultButton = new Button((_, _) => Format()) { Text = "&Format" };
        AbortButton = new Button((_, _) => Close()) { Text = "&Cancel" };

        PositiveButtons.Add(DefaultButton);
        NegativeButtons.Add(AbortButton);

        Content = new StackLayout {
            Padding = 10,
            Spacing = 10,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Items = {
                new StackLayout {
                    Spacing = 10,
                    Orientation = Orientation.Horizontal,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = { new Label { Text = "Select Project Root Folder" }, projectRootButton }
                },

                new StackLayout {
                    Spacing = 10,
                    Orientation = Orientation.Horizontal,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = { new Label { Text = "Format Room Label Indices" }, editRoomIndices }
                },
                // NOTE: The only reason Scrollables are used, is because they provide a border
                new Scrollable { Content = autoRoomIndexingLayout, Padding = 5 }.FixBorder(),

                new StackLayout {
                    Spacing = 10,
                    Orientation = Orientation.Horizontal,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = { new Label { Text = "Format Commands" }, editCommands }
                },
                new Scrollable { Content = commandLayout, Padding = 5 }.FixBorder(),
            }
        };
        Resizable = false;

        Studio.RegisterDialog(this);
        Load += (_, _) => Studio.Instance.WindowCreationCallback(this);
        Shown += (_, _) => Location = Studio.Instance.Location + new Point((Studio.Instance.Width - Width) / 2, (Studio.Instance.Height - Height) / 2);
    }

    private void Format() {
        string[] files = Directory.GetFiles(projectRoot, "*.tas", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.Hidden });

        bool formatRoomIndices = Application.Instance.Invoke(() => editRoomIndices.Checked == true);
        bool formatCommands = Application.Instance.Invoke(() => editCommands.Checked == true);

        bool includeReads = Application.Instance.Invoke(() => roomIndexType.SelectedKey == nameof(AutoRoomIndexing.IncludeReads));
        int startIndex = (int)Application.Instance.Invoke(() => startingIndex.Value);

        bool forceCase = Application.Instance.Invoke(() => forceCorrectCasing.Checked == true);
        string separator = Application.Instance.Invoke(() => argumentSeparator.Text);

        int totalTasks = 0, finishedTasks = 0;

        Label progressLabel;
        ProgressBar progressBar;
        Button doneButton;

        var progressPopup = new Eto.Forms.Dialog {
            Title = "Processing...",
            Icon = Assets.AppIcon,

            Content = new StackLayout {
                Padding = 10,
                Spacing = 10,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Items = {
                    (progressLabel = new Label { Text = $"Formatting files {finishedTasks} / {totalTasks}..." }),
                    (progressBar = new ProgressBar { Width = 300 }),
                    (doneButton = new Button { Text = "Done", Enabled = false }),
                },
            },

            Resizable = false,
            Closeable = false,
            ShowInTaskbar = true,
        };
        doneButton.Click += (_, _) => {
            progressPopup.Close();
            Close();
        };

        Studio.RegisterDialog(progressPopup);
        progressPopup.Load += (_, _) => Studio.Instance.WindowCreationCallback(progressPopup);
        progressPopup.Shown += (_, _) => progressPopup.Location = Location + new Point((Width - progressPopup.Width) / 2, (Height - progressPopup.Height) / 2);

        foreach (string file in files) {
            if (Directory.Exists(file)) {
                continue;
            }

            totalTasks++;
            Task.Run(() => {
                Console.WriteLine($"Reformatting '{file}'...");

                try {
                    if (formatRoomIndices) {
                        FileRefactor.FixRoomLabelIndices(file, includeReads ? AutoRoomIndexing.IncludeReads : AutoRoomIndexing.CurrentFile, startIndex);
                    }
                    if (formatCommands) {
                        FileRefactor.FormatFile(file, forceCase, separator);
                    }

                    Console.WriteLine($"Successfully reformatted '{file}'");
                } catch (Exception ex) {
                    Console.WriteLine($"Failed reformatted '{file}': {ex}");
                }

                finishedTasks++;
                Application.Instance.Invoke(UpdateProgress);
            });
        }

        UpdateProgress();
        progressPopup.ShowModal();

        return;

        void UpdateProgress() {
            progressLabel.Text = finishedTasks == totalTasks
                ? $"Successfully formatted {progressBar.MaxValue} files."
                : $"Formatting files {progressBar.Value} / {progressBar.MaxValue}...";
            progressBar.Value = finishedTasks;
            progressBar.MaxValue = totalTasks;

            if (finishedTasks == totalTasks) {
                doneButton.Enabled = true;
                progressPopup.Title = "Complete";
            }
        }
    }

    public static void Show() => new ProjectFileFormatterDialog().ShowModal();
}
