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
        /*
        MeshEngineer me = new MeshEngineer();
        string path = "C:/SteamGames/steamapps/common/Kenshi/mods/Grineer Race Mod/items/armour/meshes/Marine/Grineer_Lancer_Pants_M.mesh";

        me.LoadMeshFile(path);
        CoreUtils.Print(me.getSkeletonLink());//0.15
        CoreUtils.Print($"Intersection Ratio: {me.IntersectionRatio(new float[] { -0.736383f, -0.349374f, 9.11016f }, new float[] { 0.736383f, -1.81043f, 7.50407f })}");
        CoreUtils.Print($"Intersects Ratio: {me.Intersects(new float[] { -0.736383f, -0.349374f, 9.11016f }, new float[] { 0.736383f, -1.81043f, 7.50407f })}");
        */
        Logger.Mute("MeshChunk.cs");
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}