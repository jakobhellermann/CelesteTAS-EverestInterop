using TAS.Input;

namespace CelesteStudio.Communication.LibTAS.TAS;

public class InputHelper {
    public static InputFrame? LastInputFrame = null;
    public static uint? Framerate = null;
    
    public static void FeedInputs(InputFrame frame) {
        LastInputFrame = frame;
    }
}
