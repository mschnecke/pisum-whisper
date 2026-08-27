using System.Reflection;

namespace Pisum.Whisper.Spikes;

internal static class ApiDump
{
    public static int Run(string[] args)
    {
        var asmName = args.ElementAtOrDefault(1) ?? "SharpHook";
        var filter = args.ElementAtOrDefault(2) ?? "";
        var asm = Assembly.Load(asmName);

        foreach (var t in asm.GetExportedTypes()
                     .Where(t => t.FullName!.Contains(filter, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(t => t.FullName))
        {
            Console.WriteLine($"== {t.FullName}{(t.IsInterface ? " (interface)" : t.IsEnum ? " (enum)" : "")}");
            if (t.IsEnum) { Console.WriteLine("   " + string.Join(", ", Enum.GetNames(t).Take(40))); continue; }

            foreach (var c in t.GetConstructors())
                Console.WriteLine($"   .ctor({string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
            foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | (args.Length > 3 ? default : BindingFlags.DeclaredOnly)))
            {
                if (m is MethodInfo mi)
                {
                    if (mi.IsSpecialName) continue;
                    Console.WriteLine($"   {mi.ReturnType.Name} {mi.Name}({string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
                }
                else if (m is PropertyInfo pi) Console.WriteLine($"   prop {pi.PropertyType.Name} {pi.Name}");
                else if (m is EventInfo ei) Console.WriteLine($"   event {ei.EventHandlerType?.Name} {ei.Name}");
                else if (m is FieldInfo fi && fi.IsPublic) Console.WriteLine($"   field {fi.FieldType.Name} {fi.Name}");
            }
        }
        return 0;
    }
}
