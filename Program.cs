using KenshiCore;
using KenshiCore.OgreEngineering;
using KenshiCore.Utilities;
using KenshiPatcher.Forms;
using System.Globalization;
using static KenshiCore.OgreEngineering.SkeletonEngineer;

namespace KenshiPatcher;

static class Program
{
    [STAThread]
    static void Main()
    {
        Logger.Mute("MeshChunk.cs");
        //MeshEngineer me = new MeshEngineer();
        //string path = "c:/steamgames/steamapps/workshop/content/233860/1745686349/items/armour/meshes/T-8armour_Male_New.mesh";
        //Bones 17, 3, 8, 25, 15, 27, 12, 13 influences vertices in mesh T-8armour_Male_New.mesh
        //Influenced by Bone 0: False


        //string path = "C:/SteamGames/steamapps/workshop/content/233860/3431094535/items/armour/meshes/KNIVES_Male.mesh";

        //c:\steamgames\steamapps\workshop\content\233860\1745686349\items\armour\meshes\T-8armour_Male_New.mesh
        //me.LoadMeshFile(path);

        //CoreUtils.Print($"Influenced by Bone 28: {me.IsInfluencedByBone(28)}");
        //CoreUtils.Print($"get influence is: {me.GetBoneInfluence(28)}");
        //CoreUtils.Print($"get influence is: {me.GetBoneInfluence(18)}");
        //CoreUtils.Print($"Influence info is: {me.GetBoneInfluenceInfo()}");
        //CoreUtils.Print(me.getSkeletonLink());//0.15
        //CoreUtils.Print($"Intersection Ratio: {me.IntersectionRatio(new float[] { -0.736383f, -0.349374f, 9.11016f }, new float[] { 0.736383f, -1.81043f, 7.50407f })}");
        //CoreUtils.Print($"Does Intersects: {me.Intersects(new float[] { -0.991338f, -1.25691f, 16.8542f }, new float[] { 0.991338f, 1.62372f, 16.1415f }, false)}");
        //CoreUtils.Print($"Does Intersects: {me.Intersects(new float[] { -0.991338f, -1.25691f, 16.8542f }, new float[] { 0.991338f, 1.62372f, 16.1415f }, false)}");


        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}