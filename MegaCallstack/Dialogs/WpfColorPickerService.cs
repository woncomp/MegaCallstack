using System.Windows;
using System.Windows.Media;
using MegaCallstack.Dialogs;
using MegaCallstack.Services;

namespace MegaCallstack.Services
{
    public class WpfColorPickerService : IColorPickerService
    {
        private readonly Window _owner;

        public WpfColorPickerService(Window owner)
        {
            _owner = owner;
        }

        public ColorPickerResultData PickColor(Color? initialColor)
        {
            var dialog = new ColorPickerDialog(initialColor)
            {
                Owner = _owner
            };

            if (dialog.ShowDialog() == true)
            {
                if (dialog.IsCleared)
                    return ColorPickerResultData.Clear;

                return ColorPickerResultData.Ok(dialog.SelectedColor);
            }

            return ColorPickerResultData.Cancel;
        }
    }
}
