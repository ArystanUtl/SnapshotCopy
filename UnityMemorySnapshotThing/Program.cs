using System.Globalization;
using System.Text;
using UMS.Analysis;
using UMS.Analysis.Structures.Objects;
using UMS.LowLevel.Structures;

namespace UnityMemorySnapshotThing;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            File.AppendAllText("log1.txt","No file specified\n");
            return;
        }

        var filePath = @"C:\Users\User123\Desktop\Projects\SS\OldSNaphots\AfterExitOnline.snap";
        var snapshotHashcode = filePath.Split("\\").Last();
        var pointIndex = snapshotHashcode.IndexOf(".", StringComparison.Ordinal);
        snapshotHashcode = snapshotHashcode.Substring(0, pointIndex);
        snapshotHashcode += ".txt";

        var start = DateTime.Now;
        File.AppendAllText("log1.txt","\n");
        File.AppendAllText("log1.txt","Reading snapshot file...\n");
        using var file = new SnapshotFile(filePath);
        File.AppendAllText("log1.txt",$"Read snapshot file in {(DateTime.Now - start).TotalMilliseconds} ms\n\n");
        
        File.AppendAllText("log1.txt",$"Snapshot file version: {file.SnapshotFormatVersion} ({(int) file.SnapshotFormatVersion})\n");
        File.AppendAllText("log1.txt",$"Snapshot taken on {file.CaptureDateTime}\n");
        File.AppendAllText("log1.txt",$"Target platform: {file.ProfileTargetInfo}\n");
        File.AppendAllText("log1.txt",$"Memory info at time of snapshot: {file.ProfileTargetMemoryStats}\n");
        
        File.AppendAllText("log1.txt","\n");
        File.AppendAllText("log1.txt","Finding objects in snapshot...\n");

        file.LoadManagedObjectsFromGcRoots();
        file.LoadManagedObjectsFromStaticFields();

        File.AppendAllText("log1.txt",$"Found {file.AllManagedClassInstances.Count()} managed objects.\n");
        // FindLeakedUnityObjects(file);
        while (true)
        {
            Console.Write("\n\nWhat would you like to do now?\n1: Find leaked managed shells.\n2: Dump information on a specific object (by address).\n0: Exit\nChoice: ");

            var choice = Console.ReadLine();

            if (choice == "1")
            {
                FindLeakedUnityObjects(file);
                File.Move("log1.txt", snapshotHashcode);
            }
            else if (choice == "2")
            {
                DumpObjectInfo(file);
            }
            else if (choice == "0")
            {
                break;
            }
            else
            {
                File.AppendAllText("log1.txt", "Invalid choice.\n");
            }
        }

       
    }
    
    private static void FindLeakedUnityObjects(SnapshotFile file)
    {
        var start = DateTime.Now;
        File.AppendAllText("log1.txt","Finding leaked Unity objects...\n");
        
        //Find all the managed objects, filter to those which have a m_CachedObjectPtr field
        //Then filter to those for which that field is 0 (i.e. not pointing to a native object)
        //That gives the leaked managed shells.
        var ret = new StringBuilder();
        var str = $"Snapshot contains {file.AllManagedClassInstances.Count()} managed objects";
        File.AppendAllText("log1.txt",str+"\n");
        ret.AppendLine(str);

        var filterStart = DateTime.Now;

        var unityEngineObjects = file.AllManagedClassInstances.Where(i => i.InheritsFromUnityEngineObject(file)).ToArray();

        str = $"Of those, {unityEngineObjects.Length} inherit from UnityEngine.Object (filtered in {(DateTime.Now - filterStart).TotalMilliseconds} ms)";
        File.AppendAllText("log1.txt",str+"\n");
        ret.AppendLine(str);
        
        var detectStart = DateTime.Now;

        int numLeaked = 0;
        var leakedTypes = new Dictionary<string, int>();
        foreach (var managedClassInstance in unityEngineObjects)
        {
            if (managedClassInstance.IsLeakedManagedShell(file))
            {
                var typeName = file.GetTypeName(managedClassInstance.TypeInfo.TypeIndex);

                str = $"Found leaked managed object of type: {typeName} at memory address 0x{managedClassInstance.ObjectAddress:X}";
                File.AppendAllText("log1.txt",str+"\n");
                ret.AppendLine(str);

                str = $"    Retention Path: {managedClassInstance.GetFirstObservedRetentionPath(file)}";
                File.AppendAllText("log1.txt",str+"\n");
                ret.AppendLine(str);
                        
                leakedTypes[typeName] = leakedTypes.GetValueOrDefault(typeName) + 1;
                        
                numLeaked++;
            }
        }

        str = $"Finished detection in {(DateTime.Now - detectStart).TotalMilliseconds} ms. {numLeaked} of those are leaked managed shells";
        File.AppendAllText("log1.txt",str+"\n");
        ret.AppendLine(str);
        
        var leakedTypesSorted = leakedTypes.OrderByDescending(kvp => kvp.Value).ToArray();
        
        str = $"Leaked types by count: \n{string.Join("\n", leakedTypesSorted.Select(kvp => $"{kvp.Value} x {kvp.Key}"))}";
        ret.AppendLine(str);
        
        // File.WriteAllText("leaked_objects.txt", ret.ToString());
    }
    
    private static void DumpObjectInfo(SnapshotFile file)
    {
        File.AppendAllText("log1.txt","Enter the memory address of the object you want to dump:\n");
        var addressString = Console.ReadLine();
        
        if (!ulong.TryParse(addressString, NumberStyles.HexNumber, null, out var address))
        {
            File.AppendAllText("log1.txt","Unable to parse address.\n");
            return;
        }

        var nullableObj = file.TryFindManagedClassInstanceByAddress(address);
        
        if (nullableObj == null)
        {
            File.AppendAllText("log1.txt",$"No object at address 0x{address:X8} was found in the snapshot\n");
            return;
        }

        var obj = nullableObj.Value;

        if ((obj.TypeDescriptionFlags & TypeFlags.Array) != 0)
        {
            File.AppendAllText("log1.txt","Dumping arrays is not supported, yet.\n");
            return;
        }
        
        File.AppendAllText("log1.txt",$"Found object at address 0x{address:X8}\n");
        File.AppendAllText("log1.txt",$"Type: {file.GetTypeName(obj.TypeInfo.TypeIndex)}\n");
        File.AppendAllText("log1.txt",$"Flags: {obj.TypeDescriptionFlags}\n");
        File.AppendAllText("log1.txt",$"Retention path: {obj.GetFirstObservedRetentionPath(file)}\n");
        File.AppendAllText("log1.txt","Fields:\n");

        for (var i = 0; i < obj.Fields.Length; i++)
        {
            WriteField(file, obj, i);
        }
    }

    private static void WriteField(SnapshotFile file, ManagedClassInstance parent, int index)
    {
        var fields = file.GetInstanceFieldInfoForTypeIndex(parent.TypeInfo.TypeIndex);
        var fieldInfo = fields[index];
        var fieldValue = parent.Fields[index];

        var fieldName = file.GetFieldName(fieldInfo.FieldIndex);
        var fieldType = file.GetTypeName(fieldInfo.TypeDescriptionIndex);
        
        File.AppendAllText("log1.txt",$"    {fieldType} {fieldName} = {fieldValue}\n");
    }

   
}