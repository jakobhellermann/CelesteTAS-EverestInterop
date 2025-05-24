using TAS.Input;

namespace CelesteStudio.Communication.LibTAS.TAS;

public class InputHelper {
    public static InputFrame LastInputFrame;
    public static void FeedInputs(InputFrame frame) {
        LastInputFrame = frame;
    }
}
