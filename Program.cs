using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using System;
using System.IO;
using System.Reflection;

namespace Decryptor
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: program.exe <path_to_dll> <method_token>");
                Console.ReadLine();
                return;
            }

            if (!uint.TryParse(args[1], System.Globalization.NumberStyles.HexNumber, null, out uint token))
            {
                Console.WriteLine("Cant parse token");
                return;
            }

            string path = args[0];
            var module = ModuleDefMD.Load(path);

            var decryptMethod = module.ResolveMethod(token & 16777215U);
            if (decryptMethod == null)
            {
                Console.WriteLine("Method not found");
                return;
            }

            var asm = Assembly.LoadFrom(path);
            var methodInfo = asm.ManifestModule.ResolveMethod((int)token);
            var invokeArgs = new object[1];

            int replaced = 0;
            int errors = 0;

            foreach (var type in module.GetTypes())
            {
                foreach (var methodDef in type.Methods)
                {
                    var body = methodDef.Body;
                    if (body == null)
                        continue;

                    var insts = body.Instructions;
                    for (int i = 1; i < insts.Count; i++)
                    {
                        var inst = insts[i];
                        if (inst.OpCode.Code != Code.Call || inst.Operand != decryptMethod)
                            continue;

                        inst = insts[i - 1];
                        if (!inst.IsLdcI4())
                            continue;

                        try
                        {
                            invokeArgs[0] = inst.GetLdcI4Value();
                            inst.Operand = methodInfo.Invoke(null, invokeArgs);
                            inst.OpCode = OpCodes.Ldstr;
                            replaced++;

                            insts[i].OpCode = OpCodes.Nop;
                        }
                        catch (Exception)
                        {
                            errors++;
                        }
                    }
                }
            }

            Console.WriteLine("Replaced " + replaced);
            Console.WriteLine("Errors " + errors);

            Console.WriteLine("Saving...");
            var options = new ModuleWriterOptions(module);
            options.MetadataOptions.Flags |= MetadataFlags.KeepOldMaxStack | MetadataFlags.PreserveAll;
            options.Logger = DummyLogger.NoThrowInstance;
            module.Write(Path.GetFileNameWithoutExtension(path) + "_decr" + Path.GetExtension(path), options);
            Console.WriteLine("Saved!");
        }
    }
}