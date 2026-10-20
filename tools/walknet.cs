// Walking network from OSM highway ways (OPL from `osmium add-locations-to-ways -f opl`, stdin) → walk_edge rows (stdout, COPY text):
// source, target (OSM node ids), length_m, EWKT linestring. Ways are split at every node shared with another way (and at their ends),
// so edges meet only at junctions. Called by tools/build-walknet.sh.
using System.Globalization;

var ways = new List<(long[] Ids, double[] X, double[] Y)>();
var uses = new Dictionary<long, int>();
long read = 0, dropped = 0;
using var stdin = new StreamReader(Console.OpenStandardInput(), bufferSize: 1 << 20);
string? line;
while ((line = stdin.ReadLine()) != null)
{
    if (line.Length == 0 || line[0] != 'w') continue;
    read++;
    string tags = "", nodes = "";
    foreach (var f in line.Split(' '))
        if (f.StartsWith('T')) tags = f[1..];
        else if (f.StartsWith('N')) nodes = f[1..];
    if (!Walkable(Tags(tags)) || nodes.Length == 0) { dropped++; continue; }
    var ns = nodes.Split(',');
    var ids = new long[ns.Length]; var xs = new double[ns.Length]; var ys = new double[ns.Length];
    var ok = ns.Length >= 2;
    for (var i = 0; ok && i < ns.Length; i++)
    {
        // n<id>x<lon>y<lat>; a node without location has empty x/y
        var n = ns[i]; int ix = n.IndexOf('x'), iy = n.IndexOf('y');
        ok = ix > 1 && iy > ix + 1 && iy < n.Length - 1
             && long.TryParse(n.AsSpan(1, ix - 1), CultureInfo.InvariantCulture, out ids[i])
             && double.TryParse(n.AsSpan(ix + 1, iy - ix - 1), CultureInfo.InvariantCulture, out xs[i])
             && double.TryParse(n.AsSpan(iy + 1), CultureInfo.InvariantCulture, out ys[i]);
    }
    if (!ok) { dropped++; continue; }
    ways.Add((ids, xs, ys));
    for (var i = 0; i < ids.Length; i++) uses[ids[i]] = uses.GetValueOrDefault(ids[i]) + (i == 0 || i == ids.Length - 1 ? 2 : 1);
}

using var stdout = new StreamWriter(Console.OpenStandardOutput(), bufferSize: 1 << 20) { NewLine = "\n" };
var sb = new System.Text.StringBuilder();
long edges = 0;
foreach (var (ids, xs, ys) in ways)
{
    var start = 0;
    var len = 0.0;
    for (var i = 1; i < ids.Length; i++)
    {
        len += Haversine(xs[i - 1], ys[i - 1], xs[i], ys[i]);
        if (i < ids.Length - 1 && uses[ids[i]] < 2) continue;
        if (ids[start] != ids[i])   // a loop back to its own start never shortens a path
        {
            sb.Clear().Append(ids[start]).Append('\t').Append(ids[i]).Append('\t').Append(Math.Round(len, 1).ToString(CultureInfo.InvariantCulture)).Append("\tSRID=4326;LINESTRING(");
            for (var j = start; j <= i; j++)
                sb.Append(j > start ? "," : "").Append(xs[j].ToString("0.#######", CultureInfo.InvariantCulture)).Append(' ').Append(ys[j].ToString("0.#######", CultureInfo.InvariantCulture));
            stdout.WriteLine(sb.Append(')'));
            edges++;
        }
        start = i; len = 0;
    }
}
Console.Error.WriteLine($"walknet: {read} ways read, {dropped} dropped, {ways.Count} kept, {uses.Count} nodes, {edges} edges");

static Dictionary<string, string> Tags(string t)
{
    var d = new Dictionary<string, string>();
    foreach (var kv in t.Split(','))
        if (kv.IndexOf('=') is var e and > 0) d[kv[..e]] = kv[(e + 1)..];
    return d;
}

// foot=no / access=no without a foot permission go; private roads stay (marina premises are usually access=private and open to
// their guests). Trunk roads only with a foot permission or a sidewalk drawn on the road (sidewalk=separate: the footway is its own way).
static bool Walkable(Dictionary<string, string> t)
{
    var foot = t.GetValueOrDefault("foot");
    if (foot is "no" or "use_sidepath") return false;
    var footOk = foot is "yes" or "designated" or "permissive" or "destination";
    if (!footOk && t.GetValueOrDefault("access") == "no") return false;
    if (t.GetValueOrDefault("highway") is "trunk" or "trunk_link")
        return footOk || new[] { "sidewalk", "sidewalk:both", "sidewalk:left", "sidewalk:right" }.Any(k => t.GetValueOrDefault(k) is "both" or "left" or "right" or "yes");
    return true;
}

static double Haversine(double x1, double y1, double x2, double y2)
{
    const double r = Math.PI / 180;
    var a = Math.Pow(Math.Sin((y2 - y1) * r / 2), 2) + Math.Cos(y1 * r) * Math.Cos(y2 * r) * Math.Pow(Math.Sin((x2 - x1) * r / 2), 2);
    return 2 * 6371008.8 * Math.Asin(Math.Sqrt(a));
}
