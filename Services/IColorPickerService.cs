using System.Windows.Media;

namespace MegaCallstack.Services
{
    public enum ColorPickerResult
    {
        Ok,
        Clear,
        Cancel
    }

    public struct ColorPickerResultData
    {
        public ColorPickerResult Result { get; set; }
        public Color? Color { get; set; }

        public static ColorPickerResultData Clear => new ColorPickerResultData { Result = ColorPickerResult.Clear };
        public static ColorPickerResultData Cancel => new ColorPickerResultData { Result = ColorPickerResult.Cancel };
        public static ColorPickerResultData Ok(Color? color) => new ColorPickerResultData { Result = ColorPickerResult.Ok, Color = color };
    }

    public interface IColorPickerService
    {
        ColorPickerResultData PickColor(Color? initialColor);
    }
}
