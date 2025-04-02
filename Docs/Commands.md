### Read
- `Read, File Name, Starting Line, (Optional Ending Line)`
- Will read inputs from the specified file.
- If a custom path to read files from has been specified, it tries to find the file there. Otherwise, it will look for the file in the main Celeste directory.
- e.g. `Read, 1A - Forsaken City.tas, 6` will read all inputs after line 6 from the `1A - Forsaken City.tas` file
- This will also work if you shorten the file name, i.e. `Read, 1A, 6` will do the same 
- It's recommended to use labels instead of line numbers, so `Read, 1A, lvl_1` would be the preferred format for this example.

### Play
- `Play, Starting Line, (Optional Frames to Wait)`
- A simplified `Read` command which skips to the starting line in the current file.
- Useful for splitting a large level into smaller chunks.

### Repeat and EndRepeat
- Repeat the inputs between `Repeat` and `EndRepeat` several times, nesting is not supported.
- `Repeat, Count`
- `EndRepeat`

### Labels
- Prefixing a line with `#` will comment out the line
- A line beginning with `#` can be also be used as the starting point or ending point of a Read instruction.
- You can comment highlighted text in Celeste Studio by hitting `Ctrl+K`

### Load load
- `load scene positionX positionY` (insert automatically by pressing `Ctrl-Shift-R`)
  
### Set
- `Set, Type.StaticField, Values`
- `Set, MonoBehaviourType.Field, Values`
- `Set, Player.isOnLedge, true`
- Sets the specified setting to the specified value.

### Invoke
- `Invoke, Player.Method, Parameter1, Parameter2...` (all entities)

### EvalLua
- Evaluate lua code, [check out how to access and use C# objects](https://github.com/EverestAPI/ModResources/wiki/Lua-Cutscenes-Recipe-Book#accessing-and-using-c-objects)
- Due to the limitations of NLua, non-public members and generics are not supported, so some helper methods and predefined variables are provided. check the [env.lua](https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/master/CelesteTAS-EverestInterop/Source/EverestInterop/Lua/env.lua) file for details.
- `EvalLua, player.Position = player.Position + Vector2(1, 0)`
- `EvalLua, player:Die(Vector2.Zero)`
- `EvalLua, setValue(player, 'movementCounter', Vector2(0.1, 0))`

### RecordCount
- e.g. `RecordCount: 1`
- Every time you run tas after modifying the current input file, the record count auto increases by one.

### FileTime
- e.g. `FileTime: 0:51.170(3010)`
- Auto update the file time when TAS has finished running, the file time is equal to the elapsed time during the TAS run.

### ChapterTime
- e.g. `ChapterTime: 0:49.334(2902)`
- After completing the whole level from the beginning, auto updating the chapter time.

### MidwayFileTime / MidwayChapterTime
- e.g. `MidwayFileTime: 1:04.107(3771)`
- e.g. `MidwayChapterTime: 1:41.677(5981)`
- Same as `FileTime`/`ChapterTime`, except it updates when the command is executed. 
