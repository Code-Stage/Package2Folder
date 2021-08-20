using CodeStage.PackageToFolder;
using UnityEditor;

public class CheckAPI 
{
	[MenuItem("Tools/Test")]
	public static void Test()
	{
		Package2Folder.ImportPackageToFolder(@"D:\1.unitypackage", @"Assets/Wow", false);
	}
}
