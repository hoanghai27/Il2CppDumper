using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using static Il2CppDumper.DefineConstants;

namespace Il2CppDumper
{
    class Program
    {
        private static Metadata metadata;
        private static Il2Cpp il2cpp;
        private static Config config;
        private static Dictionary<Il2CppMethodDefinition, string> methodModifiers = new Dictionary<Il2CppMethodDefinition, string>();

		private const string il2cppPath = "libil2cpp.so"; // "/data/bots/MapleM/base4/lib/x86/libil2cpp.so";
		private const string metadataPath = "global-metadata.dat"; // "/data/bots/MapleM/base4/assets/bin/Data/Managed/Metadata/global-metadata.dat";
		private const string outputJsonPath = "protocol_classes.json";

        [STAThread]
        static void Main(string[] args)
        {
            config = File.Exists("config.json") ? new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText("config.json")) : new Config();
            var il2cppfile = File.ReadAllBytes(args.Length > 0 ? args[0] : il2cppPath);
            try
            {
                Console.WriteLine("Initializing metadata...");
                metadata = new Metadata(new MemoryStream(File.ReadAllBytes(args.Length > 1 ? args[1] : metadataPath)));
                Console.Clear();
                //判断il2cpp的magic
                var il2cppMagic = BitConverter.ToUInt32(il2cppfile, 0);
                var isElf = false;
                var isPE = false;
                var is64bit = false;
                switch (il2cppMagic)
                {
                    default:
                        throw new Exception("ERROR: il2cpp file not supported.");
                    case 0x905A4D://PE
                        isPE = true;
                        goto case 0xFEEDFACE;
                    case 0x464c457f://ELF
                        isElf = true;
                        if (il2cppfile[4] == 2)
                        {
                            goto case 0xFEEDFACF;//ELF64
                        }
                        goto case 0xFEEDFACE;
                    case 0xCAFEBABE://FAT header
                    case 0xBEBAFECA:
                        var machofat = new MachoFat(new MemoryStream(il2cppfile));
                        Console.Write("Select Platform: ");
                        for (var i = 0; i < machofat.fats.Length; i++)
                        {
                            var fat = machofat.fats[i];
                            Console.Write(fat.magic == 0xFEEDFACF ? $"{i + 1}.64bit " : $"{i + 1}.32bit ");
                        }
                        Console.WriteLine();
                        var key = Console.ReadKey(true);
                        var index = int.Parse(key.KeyChar.ToString()) - 1;
                        var magic = machofat.fats[index % 2].magic;
                        il2cppfile = machofat.GetMacho(index);
                        if (magic == 0xFEEDFACF)// 64-bit mach object file
                            goto case 0xFEEDFACF;
                        else
                            goto case 0xFEEDFACE;
                    case 0xFEEDFACF:// 64-bit mach object file
                        is64bit = true;
                        goto case 0xFEEDFACE;
                    case 0xFEEDFACE:// 32-bit mach object file
                        //Console.WriteLine("Select Mode: 1.Manual 2.Auto 3.Auto(Advanced) 4.Auto(Plus) 5.Auto(Symbol)");
                        key = new ConsoleKeyInfo('5', new ConsoleKey(), false, false, false); //Console.ReadKey(true);
                        var version = config.ForceIl2CppVersion ? config.ForceVersion : metadata.version;
                        Console.WriteLine("Initializing il2cpp file...");
                        if (isPE)
                        {
                            il2cpp = new PE(new MemoryStream(il2cppfile), version, metadata.maxMetadataUsages);
                        }
                        else if (isElf)
                        {
                            if (is64bit)
                                il2cpp = new Elf64(new MemoryStream(il2cppfile), version, metadata.maxMetadataUsages);
                            else
                                il2cpp = new Elf(new MemoryStream(il2cppfile), version, metadata.maxMetadataUsages);
                        }
                        else if (is64bit)
                            il2cpp = new Macho64(new MemoryStream(il2cppfile), version, metadata.maxMetadataUsages);
                        else
                            il2cpp = new Macho(new MemoryStream(il2cppfile), version, metadata.maxMetadataUsages);
                        switch (key.KeyChar)
                        {
                            case '2':
                            case '3':
                            case '4':
                            case '5':
                                try
                                {
                                    Console.WriteLine("Searching...");
                                    if (key.KeyChar == '2' ? !il2cpp.Search() :
                                        key.KeyChar == '3' ? !il2cpp.AdvancedSearch(metadata.methodDefs.Count(x => x.methodIndex >= 0)) :
                                        key.KeyChar == '4' ? !il2cpp.PlusSearch(metadata.methodDefs.Count(x => x.methodIndex >= 0), metadata.typeDefs.Length) :
                                        !il2cpp.SymbolSearch())
                                    {
                                        throw new Exception();
                                    }
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"{e.Message}\r\n{e.StackTrace}\r\n");
                                    throw new Exception("ERROR: Can't use this mode to process file, try another mode.");
                                }
                                break;
                            case '1':
                                {
                                    Console.Write("Input CodeRegistration: ");
                                    var codeRegistration = Convert.ToUInt64(Console.ReadLine(), 16);
                                    Console.Write("Input MetadataRegistration: ");
                                    var metadataRegistration = Convert.ToUInt64(Console.ReadLine(), 16);
                                    il2cpp.Init(codeRegistration, metadataRegistration);
                                    break;
                                }
                            default:
                                return;
                        }
                        //DummyDll
                        if (config.DummyDll)
                        {
                            //Console.WriteLine("Create DummyDll...");
                            //if (Directory.Exists("DummyDll"))
                            //    Directory.Delete("DummyDll", true);
                            //Directory.CreateDirectory("DummyDll");
                            //Directory.SetCurrentDirectory("DummyDll");
                            //File.WriteAllBytes("Il2CppDummyDll.dll", Resource1.Il2CppDummyDll);
                            Console.WriteLine("Searching for MSM protocol-related classes ...");
							var dummy = new MSMProtocolCreator(metadata, il2cpp, args.Length > 2 ? args[2] : outputJsonPath);
                            //foreach (var assembly in dummy.Assemblies)
                            //{
                            //    var stream = new MemoryStream();
                            //    assembly.Write(stream);
                            //    File.WriteAllBytes(assembly.MainModule.Name, stream.ToArray());
                            //}
                            Console.WriteLine("Done !");
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"{e.Message}\r\n{e.StackTrace}");
            }
            //Console.WriteLine("Press any key to exit...");
            //Console.ReadKey(true);
        }

        private static string GetTypeName(Il2CppType pType)
        {
            string ret;
            switch (pType.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    {
                        var klass = metadata.typeDefs[pType.data.klassIndex];
                        ret = metadata.GetStringFromIndex(klass.nameIndex);
                        break;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        var generic_class = il2cpp.MapVATR<Il2CppGenericClass>(pType.data.generic_class);
                        var pMainDef = metadata.typeDefs[generic_class.typeDefinitionIndex];
                        ret = metadata.GetStringFromIndex(pMainDef.nameIndex);
                        var typeNames = new List<string>();
                        var pInst = il2cpp.MapVATR<Il2CppGenericInst>(generic_class.context.class_inst);
                        var pointers = il2cpp.GetPointers(pInst.type_argv, (long)pInst.type_argc);
                        for (uint i = 0; i < pInst.type_argc; ++i)
                        {
                            var pOriType = il2cpp.GetIl2CppType(pointers[i]);
                            typeNames.Add(GetTypeName(pOriType));
                        }
                        ret += $"<{string.Join(", ", typeNames)}>";
                        break;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                    {
                        var arrayType = il2cpp.MapVATR<Il2CppArrayType>(pType.data.array);
                        var type = il2cpp.GetIl2CppType(arrayType.etype);
                        ret = $"{GetTypeName(type)}[{new string(',', arrayType.rank - 1)}]";
                        break;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    {
                        var type = il2cpp.GetIl2CppType(pType.data.type);
                        ret = $"{GetTypeName(type)}[]";
                        break;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    {
                        var type = il2cpp.GetIl2CppType(pType.data.type);
                        ret = $"{GetTypeName(type)}*";
                        break;
                    }
                default:
                    ret = TypeString[(int)pType.type];
                    break;
            }

            return ret;
        }

        private static string GetCustomAttribute(int index, string padding = "")
        {
            if (!config.DumpAttribute || il2cpp.version < 21)
                return "";
            var attributeTypeRange = metadata.attributeTypeRanges[index];
            var sb = new StringBuilder();
            for (var i = 0; i < attributeTypeRange.count; i++)
            {
                var typeIndex = metadata.attributeTypes[attributeTypeRange.start + i];
                sb.AppendFormat("{0}[{1}] // 0x{2:X}\n", padding, GetTypeName(il2cpp.types[typeIndex]), il2cpp.customAttributeGenerators[index]);
            }
            return sb.ToString();
        }

        private static string GetModifiers(Il2CppMethodDefinition methodDef)
        {
            if (methodModifiers.TryGetValue(methodDef, out string str))
                return str;
            var access = methodDef.flags & METHOD_ATTRIBUTE_MEMBER_ACCESS_MASK;
            switch (access)
            {
                case METHOD_ATTRIBUTE_PRIVATE:
                    str += "private ";
                    break;
                case METHOD_ATTRIBUTE_PUBLIC:
                    str += "public ";
                    break;
                case METHOD_ATTRIBUTE_FAMILY:
                    str += "protected ";
                    break;
                case METHOD_ATTRIBUTE_ASSEM:
                case METHOD_ATTRIBUTE_FAM_AND_ASSEM:
                    str += "internal ";
                    break;
                case METHOD_ATTRIBUTE_FAM_OR_ASSEM:
                    str += "protected internal ";
                    break;
            }
            if ((methodDef.flags & METHOD_ATTRIBUTE_STATIC) != 0)
                str += "static ";
            if ((methodDef.flags & METHOD_ATTRIBUTE_ABSTRACT) != 0)
            {
                str += "abstract ";
                if ((methodDef.flags & METHOD_ATTRIBUTE_VTABLE_LAYOUT_MASK) == METHOD_ATTRIBUTE_REUSE_SLOT)
                    str += "override ";
            }
            else if ((methodDef.flags & METHOD_ATTRIBUTE_FINAL) != 0)
            {
                if ((methodDef.flags & METHOD_ATTRIBUTE_VTABLE_LAYOUT_MASK) == METHOD_ATTRIBUTE_REUSE_SLOT)
                    str += "sealed override ";
            }
            else if ((methodDef.flags & METHOD_ATTRIBUTE_VIRTUAL) != 0)
            {
                if ((methodDef.flags & METHOD_ATTRIBUTE_VTABLE_LAYOUT_MASK) == METHOD_ATTRIBUTE_NEW_SLOT)
                    str += "virtual ";
                else
                    str += "override ";
            }
            if ((methodDef.flags & METHOD_ATTRIBUTE_PINVOKE_IMPL) != 0)
                str += "extern ";
            methodModifiers.Add(methodDef, str);
            return str;
        }

        private static string ToEscapedString(string s)
        {
            var re = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\'':
                        re.Append(@"\'");
                        break;
                    case '"':
                        re.Append(@"\""");
                        break;
                    case '\t':
                        re.Append(@"\t");
                        break;
                    case '\n':
                        re.Append(@"\n");
                        break;
                    case '\r':
                        re.Append(@"\r");
                        break;
                    case '\f':
                        re.Append(@"\f");
                        break;
                    case '\b':
                        re.Append(@"\b");
                        break;
                    case '\\':
                        re.Append(@"\\");
                        break;
                    case '\0':
                        re.Append(@"\0");
                        break;
                    case '\u0085':
                        re.Append(@"\u0085");
                        break;
                    case '\u2028':
                        re.Append(@"\u2028");
                        break;
                    case '\u2029':
                        re.Append(@"\u2029");
                        break;
                    default:
                        re.Append(c);
                        break;
                }
            }
            return re.ToString();
        }
    }
}
