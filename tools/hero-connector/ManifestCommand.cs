using System.Text;
using System.Text.Json;
using SodRpg.Core.Game;

internal static class ManifestCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: hero-connector --manifest <source-MOD-folder>");
            return 2;
        }
        try
        {
            StarClusters.RegisterAllGenerated();
            string root = Path.GetFullPath(args[0]);
            string Read(string relative)
            {
                if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
                    throw new IOException("manifest must be a MOD-relative path");
                string path = root;
                foreach (string part in relative.Replace('\\', '/').Split('/'))
                {
                    if (part.Length == 0 || part == "." || part == "..") throw new IOException("invalid relative path");
                    path = Path.Combine(path, part);
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("symbolic links are unsupported");
                }
                using var stream = File.OpenRead(path);
                if (stream.Length > ExtendedHeroes.ManifestTextLimit) throw new IOException("connector file exceeds 4 MiB");
                using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
                var text = new StringBuilder();
                var buffer = new char[8192];
                int count;
                while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (stream.Position > ExtendedHeroes.ManifestTextLimit || text.Length + count > ExtendedHeroes.ManifestTextLimit)
                        throw new IOException("connector text exceeds 4 MiB");
                    text.Append(buffer, 0, count);
                }
                return text.ToString();
            }
            var result = ExtendedHeroes.Register(Read("dreamforge.json"), "manifest-inspector", Console.Error.WriteLine, Read);
            foreach (var hero in result.Heroes)
            {
                int count = hero.Accepted ? HeroSigils.TreeFor(hero.Key).Count : 0;
                Console.WriteLine(JsonSerializer.Serialize(new { hero.Key, hero.Accepted, hero.GenericTree, hero.Reason,
                    hero.DeclarationHash, Stars = count, Authored = hero.Accepted && StarClusters.TryGetRegisteredTree(hero.Key, out _) }));
            }
            if (result.FileError != null) Console.Error.WriteLine(result.FileError);
            return result.FileError == null && result.Heroes.Count > 0 && result.Heroes.All(h => h.Accepted) ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
