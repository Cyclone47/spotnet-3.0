using System;
using System.Linq;
using Mono.Cecil;

namespace MakePublic
{
    class Program
    {
        static void Main(string[] args)
        {
            string dll = @"d:\sourcecode\src\Spotnet\lib\Squirrel.dll";
            var asm = AssemblyDefinition.ReadAssembly(dll);
            var updateInfoType = asm.MainModule.Types.FirstOrDefault(t => t.Name == "UpdateInfo");
            Console.WriteLine($"UpdateInfo Type: {updateInfoType.FullName}, Attributes: {updateInfoType.Attributes}");
            foreach (var m in updateInfoType.Methods)
            {
                Console.WriteLine($"  Method: {m.FullName}, Attributes: {m.Attributes}");
            }
        }
    }
}
