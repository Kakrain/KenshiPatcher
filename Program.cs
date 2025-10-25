using KenshiCore;
using KenshiPatcher.Forms;

namespace KenshiPatcher;

static class Program
{
    [STAThread]
    static void Main()
    {
        //ReverseEngineer re= new ReverseEngineer();
        //re.testAll();
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }    
}