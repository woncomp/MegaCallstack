using MegaCallstack.Models;

namespace MegaCallstack.Services
{
    public interface ISettingsService
    {
        MegaCallstackSettings Current { get; }
        void Save(MegaCallstackSettings settings);
    }
}
