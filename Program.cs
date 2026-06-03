using KenshiCore;
using KenshiCore.OgreEngineering;
using KenshiCore.Utilities;
using KenshiPatcher.Forms;
using System.Globalization;

namespace KenshiPatcher;

static class Program
{
    [STAThread]
    static void Main()
    {
        Logger.Mute("MeshChunk.cs");
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}