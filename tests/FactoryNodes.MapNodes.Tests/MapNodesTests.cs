using System.Text.Json.Nodes;
using FactoryNodes.Devices;
using FactoryNodes.MapNodes.Presentation;

namespace FactoryNodes.MapNodes.Tests;

/// <summary>
/// MapNodes (PENDIENTES 200): abrir y guardar un fichero no pierde nada (todos los de <c>library/devices</c>), se escribe a mano y se pega de una hoja
/// de cálculo, y lo que no vale se dice con las mismas reglas con las que lo lee el programa.
/// </summary>
public class MapNodesTests
{
    private sealed class Services : IMapNodesServices
    {
        public Dictionary<string, string> Files { get; } = [];
        public string? Clipboard { get; set; }
        public string? SaveTo { get; set; }
        public string? ChooseFileToOpen() => Files.Keys.FirstOrDefault();
        public string? ChooseFileToSave(string suggestedName, string suggestedFolder) => SaveTo ?? $"{suggestedFolder}/{suggestedName}";
        public string ReadText(string path) => Files[path];
        public void WriteText(string path, string text) => Files[path] = text;
        public bool Confirm(string message) => true;
        public string? ClipboardText() => Clipboard;
        public string FileName(string path) => path.Split('/', '\\')[^1];
        public string? SavedLanguage { get; set; } = "es";
        public System.Globalization.CultureInfo SystemCulture => System.Globalization.CultureInfo.InvariantCulture;
        public void Restart() { }
    }

    private static string DevicesFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "library", "devices"))) return Path.Combine(dir.FullName, "library", "devices");
        throw new InvalidOperationException("No se encuentra library/devices");
    }

    public static TheoryData<string> CatalogFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(DevicesFolder(), "*.json", SearchOption.AllDirectories)) data.Add(Path.GetRelativePath(DevicesFolder(), file));
        return data;
    }

    [Theory]
    [MemberData(nameof(CatalogFiles))]
    public void Opening_and_saving_a_catalog_file_keeps_everything(string relative)
    {
        var original = File.ReadAllText(Path.Combine(DevicesFolder(), relative));
        var services = new Services();
        services.Files["a.json"] = original;
        var vm = new MapNodesViewModel(services);
        Assert.True(vm.OpenFile("a.json"));
        Assert.False(vm.HasProblems);

        // Lo mismo, campo a campo (lo nulo y lo que vale lo de siempre no se escribe: se comparan con lo que lee el programa)
        var (before, _) = DeviceModel.Parse(original);
        var (after, problems) = DeviceModel.Parse(vm.ToJson());
        Assert.Empty(problems);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(before!.ToJson()), JsonNode.Parse(after!.ToJson())));
    }

    [Fact]
    public void A_file_written_by_hand_is_valid_and_goes_to_brand_model_json()
    {
        var services = new Services();
        var vm = new MapNodesViewModel(services);
        Assert.True(vm.HasProblems);   // sin marca ni modelo
        vm.Brand = "Acme";
        vm.ModelName = "V100";
        vm.Kind = "Variador";
        vm.RegisterBase = 1;
        var group = vm.AddGroup();
        group.Text = "Rampas";
        var p = vm.AddParameter();
        p.Code = "ACC";
        p.Address = "0x1103";
        p.Type = DeviceValueType.UDINT;
        p.Scale = "0,01";
        p.Unit = "s";
        p.Group = 1;
        p.Options = "0=Lineal; 1=En S";
        var c = vm.AddCommand();
        c.Text = "Reset";
        c.Address = "0x0900";
        c.Value = 1;
        c.ResponseAddress = "0x0901";
        c.ResponseDone = 2;
        Assert.False(vm.HasProblems, string.Join(" | ", vm.Problems));
        Assert.True(vm.IsDirty);

        vm.SaveCommand.Execute(null);
        Assert.False(vm.IsDirty);
        var saved = services.Files["acme/V100.json"];
        var (model, problems) = DeviceModel.Parse(saved);
        Assert.Empty(problems);
        Assert.Equal(("Acme", "V100", 1), (model!.Brand, model.Model, model.Protocol.RegisterBase));
        var acc = Assert.Single(model.Parameters);
        Assert.Equal(("0x1103", DeviceValueType.UDINT, 0.01, 1), (acc.Address, acc.Type, acc.Scale, acc.Group));
        Assert.Equal("En S", acc.Options!["1"]);
        Assert.Equal(new DeviceGroup(1, "Rampas"), Assert.Single(model.Groups));
        Assert.Equal(new DeviceCommandResponse("0x0901", 2, 0), Assert.Single(model.Commands).Response);
        Assert.DoesNotContain("\\u00", saved);   // los acentos, tal cual
        Assert.DoesNotContain("null", saved);
    }

    [Fact]
    public void Pasting_from_a_spreadsheet_adds_new_codes_and_updates_known_ones()
    {
        var services = new Services();
        var vm = new MapNodesViewModel(services) { Brand = "Acme", ModelName = "V100" };
        var paste = "Código\tDirección\tTipo\tEscala\tUnidad\tDescripción\tGrupo\tClase\tEscribe\n" +
                    "F001\t0x1000\tUINT\t0,1\tHz\tFrecuencia\t0\tajuste\tsí\n" +
                    "D001\t0x2000\tINT\t1\tA\tIntensidad\t0\ten vivo\tno\n" +
                    "\t0x3000\n";
        Assert.Equal(2, vm.PasteParameters(paste));
        Assert.Equal(2, vm.Parameters.Count);
        var live = vm.Model.Parameter("D001")!;
        Assert.Equal((DeviceParameterKind.Live, false, DeviceValueType.INT), (live.Kind, live.Writable, live.Type));
        Assert.Equal(0.1, vm.Model.Parameter("F001")!.Scale);

        Assert.Equal(1, vm.PasteParameters("F001;0x1001;UDINT"));   // ya estaba: se cambia, no se repite
        Assert.Equal(2, vm.Parameters.Count);
        Assert.Equal(("0x1001", DeviceValueType.UDINT), (vm.Model.Parameter("F001")!.Address, vm.Model.Parameter("F001")!.Type));
        Assert.False(vm.HasProblems, string.Join(" | ", vm.Problems));
    }

    [Fact]
    public void Problems_are_the_ones_the_program_reads_with()
    {
        var vm = new MapNodesViewModel(new Services()) { Brand = "Acme", ModelName = "V100" };
        var a = vm.AddParameter();
        a.Address = "0x10";
        var b = vm.AddParameter();
        b.Code = a.Code;   // repetido
        b.Address = "nada";
        Assert.Contains(vm.Problems, p => p.Contains("repetido"));
        Assert.Contains(vm.Problems, p => p.Contains("«nada» no vale"));
        vm.RemoveParameter(b);
        Assert.False(vm.HasProblems, string.Join(" | ", vm.Problems));
    }

    [Fact]
    public void Every_button_icon_is_embedded()
    {
        foreach (var svg in new[] { MapNodesIcons.New, MapNodesIcons.Open, MapNodesIcons.Save, MapNodesIcons.SaveAs, MapNodesIcons.Paste, MapNodesIcons.AddRow,
                     MapNodesIcons.RemoveRow, MapNodesIcons.Language })
            Assert.StartsWith("<svg", svg);
    }
}
