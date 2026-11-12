using FactoryNodes.Devices;
using FactoryNodes.MapNodes.Presentation;
using FactoryNodes.Modbus;

namespace FactoryNodes.MapNodes.Tests;

/// <summary>
/// Lo que se añadió para terminar el punto 200 (autor, 2026-11-12): el tipo de la lista, la versión y el autor; los decimales que da otro
/// parámetro; el dibujo; «Probar con el equipo» y abrir un modelo de la web.
/// </summary>
public class MapNodesMoreTests
{
    private sealed class Services : IMapNodesServices
    {
        public Dictionary<string, string> Files { get; } = [];
        public Dictionary<string, byte[]> Web { get; } = [];
        public string? Drawing { get; set; }
        public string? Opened { get; private set; }
        public int ModbusPort { get; set; }
        public string? ChooseFileToOpen() => Files.Keys.FirstOrDefault();
        public string? ChooseFileToSave(string suggestedName, string suggestedFolder) => $"{suggestedFolder}/{suggestedName}";
        public string ReadText(string path) => Files[path];
        public void WriteText(string path, string text) => Files[path] = text;
        public bool Confirm(string message) => true;
        public string? ClipboardText() => null;
        public string FileName(string path) => path.Split('/', '\\')[^1];
        public string? SavedLanguage { get; set; } = "es";
        public System.Globalization.CultureInfo SystemCulture => System.Globalization.CultureInfo.InvariantCulture;
        public void Restart() { }
        public string? ChooseDrawingToOpen() => Drawing;
        public IModbusClient? Connect(string host, int port) => new ModbusTcpClient(host, ModbusPort);
        public Func<string, CancellationToken, Task<byte[]>>? HttpBytes => (url, _) => Task.FromResult(Web[url]);
        public void OpenUrl(string url) => Opened = url;
    }

    private static string Catalog(string brand, string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "library", "devices"))) return File.ReadAllText(Path.Combine(dir.FullName, "library", "devices", brand, file));
        throw new InvalidOperationException("No se encuentra library/devices");
    }

    [Fact]
    public void The_type_the_version_and_the_author_are_in_the_file()
    {
        var vm = new MapNodesViewModel(new Services()) { Brand = "Acme", ModelName = "X1" };
        Assert.Equal(["drive", "starter", "weighing", "lighting", ""], vm.TypeOptions.Select(o => o.Value));
        Assert.Equal(["Variador", "Arrancador", "Pesaje", "Iluminación", "Otros"], vm.TypeOptions.Select(o => o.Text));
        (vm.Type, vm.Version, vm.Author) = ("weighing", 3, "Pruebas S.L.");
        var model = DeviceModel.Parse(vm.ToJson()).Model!;
        Assert.Equal(("weighing", 3, "Pruebas S.L."), (model.Type, model.Version, model.Author));

        vm.Version = 0;
        Assert.Contains(vm.Problems, p => p.Contains("versión"));
    }

    [Theory]
    [InlineData("StabDec >>8 &255", "StabDec", 8, 255, null)]
    [InlineData("DspDiv: 0=3; 5=2", "DspDiv", 0, 0, "0=3;5=2")]
    [InlineData("  FlowDec  ", "FlowDec", 0, 0, null)]
    [InlineData("Reg >>4 &0x0F: 1=2, 2=1", "Reg", 4, 15, "1=2;2=1")]
    public void The_decimals_another_parameter_gives_are_written_in_one_line(string text, string from, int shift, long mask, string? map)
    {
        var decimals = ParameterRow.DecimalsOf(text)!;
        Assert.Equal((from, shift, mask), (decimals.From, decimals.Shift, decimals.Mask));
        Assert.Equal(map, decimals.Map is null ? null : string.Join(";", decimals.Map.Select(kv => $"{kv.Key}={kv.Value}")));
        Assert.Equal(decimals.From, ParameterRow.DecimalsOf(ParameterRow.DecimalsText(decimals))!.From);   // ida y vuelta
        Assert.Null(ParameterRow.DecimalsOf("   "));
    }

    [Fact]
    public void Editing_the_decimals_of_a_parameter_changes_the_file_and_a_wrong_source_is_a_problem()
    {
        var services = new Services();
        services.Files["a.json"] = Catalog("scaime", "eNod4-F.json");
        var vm = new MapNodesViewModel(services);
        vm.OpenFile("a.json");
        var weight = vm.Parameters.First(p => p.Parameter.DecimalsFrom is not null);
        Assert.Equal("StabDec >>8 &255", weight.DecimalsFrom);

        weight.DecimalsFrom = "NoExiste";
        Assert.True(vm.IsDirty);
        Assert.Contains(vm.Problems, p => p.Contains("NoExiste"));
        weight.DecimalsFrom = "";
        Assert.Null(weight.Parameter.DecimalsFrom);
        Assert.False(vm.HasProblems);
    }

    [Fact]
    public void The_drawing_is_loaded_from_an_svg_file_and_can_be_removed()
    {
        var services = new Services { Drawing = "d.svg" };
        services.Files["d.svg"] = "<?xml version=\"1.0\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\"><rect/></svg>\n";
        services.Files["no.svg"] = "hola";
        var vm = new MapNodesViewModel(services);
        Assert.False(vm.HasDrawing);
        vm.LoadDrawingCommand.Execute(null);
        Assert.StartsWith("<svg", vm.Svg);
        Assert.True(vm.HasDrawing && vm.IsDirty);

        services.Drawing = "no.svg";
        vm.LoadDrawingCommand.Execute(null);
        Assert.StartsWith("<svg", vm.Svg);   // lo que no es un SVG no sustituye al que había

        vm.RemoveDrawingCommand.Execute(null);
        Assert.False(vm.HasDrawing);
    }

    [Fact]
    public async Task Testing_with_the_device_reads_every_parameter_and_shows_it_in_its_row()
    {
        var model = DeviceModel.Parse(Catalog("utilcell", "DAT400.json")).Model!;
        await using var device = new ModbusTcpSimulator();
        device.Fill(model, p => p.Options is { Count: > 0 } o ? long.Parse(o.Keys.First()) : 0);
        (device.Registers[1], device.Registers[2]) = (0xFFFF, 0xB780);   // bruto −18560
        device.Registers[1003] = 5;                                       // división 0,05: dos decimales
        var services = new Services { ModbusPort = device.Port };
        services.Files["a.json"] = model.ToJson();
        var vm = new MapNodesViewModel(services);
        vm.OpenFile("a.json");

        await vm.TestAsync();
        Assert.Contains("IP", vm.TestText);   // sin IP no se prueba

        (vm.TestHost, vm.TestPort, vm.TestUnit) = ("127.0.0.1", "502", "1");
        await vm.TestAsync();
        Assert.Equal("-185,60", vm.Parameters.First(p => p.Code == "Gross").TestValue.Replace('.', ','));
        Assert.DoesNotContain(vm.Parameters, p => p.Address.Length > 0 && p.TestValue.Length == 0);
        Assert.False(vm.IsDirty);   // probar no cambia el fichero
    }

    [Fact]
    public async Task A_published_model_is_opened_from_the_web_only_with_the_signed_index()
    {
        var services = new Services();
        var mx2 = Catalog("omron", "MX2.json");
        var index = DeviceIndex.Build([("MX2.json", mx2)], DeviceTypes.BuiltIn, DateTimeOffset.UnixEpoch, out _).ToJson();
        services.Web[DeviceDownloads.DefaultBaseUrl + "index.json"] = System.Text.Encoding.UTF8.GetBytes(index);
        services.Web[DeviceDownloads.DefaultBaseUrl + "index.json.sig"] = System.Text.Encoding.UTF8.GetBytes("AAAA");
        var vm = new MapNodesViewModel(services);

        vm.OpenWebCommand.Execute(null);
        await vm.Web.LoadAsync();
        Assert.True(vm.Web.IsOpen);
        Assert.Empty(vm.Web.Rows);              // la firma no es la de FactoryNodes: la lista no se usa
        Assert.Contains("firma", vm.Web.Status);
    }

    [Fact]
    public void Sharing_saves_the_file_and_opens_the_portal()
    {
        var services = new Services();
        var vm = new MapNodesViewModel(services);
        vm.ShareCommand.Execute(null);
        Assert.Null(services.Opened);           // con problemas (sin marca ni modelo) no se comparte

        (vm.Brand, vm.ModelName) = ("Acme", "X1");
        vm.ShareCommand.Execute(null);
        Assert.Equal(MapNodesViewModel.ShareUrl, services.Opened);
        Assert.Contains("acme/X1.json", services.Files.Keys);
        Assert.False(vm.IsDirty);
    }
}

/// <summary>
/// Los idiomas en MapNodes (autor, 2026-11-12: «la posibilidad de generar idiomas… sin guardar los parámetros, solo idiomas»): el idioma del
/// fichero y la pestaña «Traducción», que guarda un fichero aparte solo de textos.
/// </summary>
public class MapNodesTranslationTests
{
    private sealed class Services : IMapNodesServices
    {
        public Dictionary<string, string> Files { get; } = [];
        public string? Open { get; set; }
        public string? ChooseFileToOpen() => Open;
        public string? ChooseFileToSave(string suggestedName, string suggestedFolder) => $"{suggestedFolder}/{suggestedName}";
        public string ReadText(string path) => Files[path];
        public void WriteText(string path, string text) => Files[path] = text;
        public bool Confirm(string message) => true;
        public string? ClipboardText() => null;
        public string FileName(string path) => path.Split('/')[^1];
        public string? SavedLanguage { get; set; } = "es";
        public System.Globalization.CultureInfo SystemCulture => System.Globalization.CultureInfo.InvariantCulture;
        public void Restart() { }
    }

    private static string Mx2()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "library", "devices"))) return File.ReadAllText(Path.Combine(dir.FullName, "library", "devices", "omron", "MX2.json"));
        throw new InvalidOperationException("No se encuentra library/devices");
    }

    [Fact]
    public void A_translation_is_written_with_the_original_beside_and_saved_as_a_file_of_texts_only()
    {
        var services = new Services();
        services.Files["MX2.json"] = Mx2();
        var vm = new MapNodesViewModel(services);
        vm.OpenFile("MX2.json");
        Assert.Equal("es", vm.Language);
        Assert.DoesNotContain(vm.TranslationLanguageOptions, o => o.Value == "es");   // al idioma del propio fichero no se traduce

        vm.TranslationLanguage = "de";
        vm.StartTranslationCommand.Execute(null);
        Assert.NotEmpty(vm.TranslationRows);
        var kind = vm.TranslationRows.Single(r => r.Id == "kind");
        Assert.Equal("Variador", kind.Original);
        kind.Text = "Frequenzumrichter";
        var parameter = vm.TranslationRows.First(r => r.Id.StartsWith("p|") && r.Id.EndsWith("|d"));
        parameter.Text = "Übersetzt";
        var option = vm.TranslationRows.First(r => r.Id.Contains("|o|"));
        option.Text = "Erste";
        Assert.True(vm.IsTranslationDirty);
        Assert.False(vm.IsDirty);   // traducir no toca el fichero del dispositivo

        vm.SaveTranslationCommand.Execute(null);
        var saved = services.Files["omron/MX2.de.json"];
        Assert.DoesNotContain("address", saved);   // solo textos: ni direcciones ni tipos ni escalas
        Assert.DoesNotContain("scale", saved);
        var translation = DeviceTranslation.Parse(saved).Translation!;
        Assert.Equal(("de", "Frequenzumrichter", 1), (translation.Language, translation.Kind, translation.Parameters.Count(p => p.Value.Description == "Übersetzt")));
        Assert.Equal(Mx2(), services.Files["MX2.json"]);   // el fichero base, igual

        // el catálogo enseña el modelo con esa traducción
        var catalog = new DeviceCatalog([("a", Mx2()), ("b", saved)]) { PreferredLanguage = "de" };
        Assert.Equal("Frequenzumrichter", catalog.Find("Omron MX2")!.Kind);

        // y se vuelve a abrir para seguir con ella
        var again = new MapNodesViewModel(services);
        again.OpenFile("MX2.json");
        Assert.True(again.OpenFile("omron/MX2.de.json"));
        Assert.Equal("de", again.TranslationLanguage);
        Assert.Equal(3, again.TranslationRows.Count(r => r.Text.Length > 0));
        Assert.Equal("Frequenzumrichter", again.TranslationRows.Single(r => r.Id == "kind").Text);
        Assert.False(again.IsTranslationDirty);
    }

    [Fact]
    public void A_translation_of_another_model_is_not_loaded_and_a_new_file_can_be_translated_without_saving_it()
    {
        var services = new Services();
        services.Files["MX2.json"] = Mx2();
        var other = new DeviceTranslation { TranslationOf = new DeviceTranslationTarget { Brand = "Acme", Model = "X1" }, Language = "de", Kind = "x" };
        services.Files["x.de.json"] = other.ToJson();
        var vm = new MapNodesViewModel(services);
        vm.OpenFile("MX2.json");
        Assert.False(vm.OpenFile("x.de.json"));
        Assert.Empty(vm.TranslationRows);

        // un fichero nuevo, sin guardar: se traduce lo que hay en la ventana
        var fresh = new MapNodesViewModel(services) { Brand = "Acme", ModelName = "X1", Kind = "Báscula", Language = "es" };
        fresh.TranslationLanguage = "en";
        fresh.StartTranslation();
        fresh.TranslationRows.Single(r => r.Id == "kind").Text = "Scale";
        fresh.SaveTranslationCommand.Execute(null);
        Assert.Contains("acme/X1.en.json", services.Files.Keys);
        Assert.DoesNotContain("acme/X1.json", services.Files.Keys);   // el del dispositivo no se ha guardado
    }
}
