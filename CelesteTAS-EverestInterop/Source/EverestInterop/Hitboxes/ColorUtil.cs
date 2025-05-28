using UnityEngine;
// ReSharper disable InconsistentNaming

namespace TAS.EverestInterop.Hitboxes;

public static class ColorUtil {
    public static Color OklchToColor(float L, float C, float h) {
        // Convert h from degrees to radians
        var hRad = h * Mathf.Deg2Rad;

        // Convert to OKLab
        var a = C * Mathf.Cos(hRad);
        var b = C * Mathf.Sin(hRad);

        // Convert to LMS
        var l_ = L + (0.3963377774f * a) + (0.2158037573f * b);
        var m_ = L - (0.1055613458f * a) - (0.0638541728f * b);
        var s_ = L - (0.0894841775f * a) - (1.2914855480f * b);

        // Cube LMS
        var l = l_ * l_ * l_;
        var m = m_ * m_ * m_;
        var s = s_ * s_ * s_;

        // Convert to linear RGB
        var R = (+4.0767416621f * l) - (3.3077115913f * m) + (0.2309699292f * s);
        var G = (-1.2684380046f * l) + (2.6097574011f * m) - (0.3413193965f * s);
        var B = (-0.0041960863f * l) - (0.7034186147f * m) + (1.7076147010f * s);

        // Clamp to [0, 1]
        R = Mathf.Clamp01(R);
        G = Mathf.Clamp01(G);
        B = Mathf.Clamp01(B);

        return new Color(R, G, B, 1);
    }
}
