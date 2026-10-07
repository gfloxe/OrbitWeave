using System.Diagnostics;
using System.Text;
using OrbitWeave.Actions;

namespace OrbitWeave.Tests;

public class ScriptCommandTests
{
    private static string TempScript(string extension)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("orbitweave-").FullName, "mon script" + extension);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void Visible_ps1_runs_in_powershell_and_stays_open()
    {
        var path = TempScript(".ps1");
        var info = ScriptCommand.Build(new ScriptStep { Path = path });
        Assert.Equal("powershell.exe", info.FileName);
        Assert.Equal($"-NoProfile -ExecutionPolicy Bypass -NoExit -File \"{path}\"", info.Arguments);
        Assert.Equal(Path.GetDirectoryName(path), info.WorkingDirectory);
        Assert.True(info.UseShellExecute);
    }

    [Fact]
    public void Hidden_bat_runs_in_cmd_and_closes()
    {
        var path = TempScript(".bat");
        var info = ScriptCommand.Build(new ScriptStep { Path = path, Hidden = true });
        Assert.Equal("cmd.exe", info.FileName);
        Assert.Equal($"/c \"\"{path}\"\"", info.Arguments);
        Assert.Equal(ProcessWindowStyle.Hidden, info.WindowStyle);
    }

    [Fact]
    public void Other_files_open_with_their_association()
    {
        var path = TempScript(".py");
        Assert.Equal(path, ScriptCommand.Build(new ScriptStep { Path = path }).FileName);
    }

    [Fact]
    public void PowerShell_commands_are_encoded()
    {
        const string code = "Write-Output \"l'été\"";
        var info = ScriptCommand.Build(new ScriptStep { Source = ScriptSource.Commande, Code = code, Hidden = true });
        Assert.Equal("powershell.exe", info.FileName);
        var encoded = info.Arguments.Split(' ').Last();
        Assert.StartsWith("-NoProfile -ExecutionPolicy Bypass -EncodedCommand ", info.Arguments);
        Assert.Equal(code, Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
    }

    [Fact]
    public void Cmd_commands_keep_the_window_open_when_visible()
    {
        var info = ScriptCommand.Build(new ScriptStep { Source = ScriptSource.Commande, Code = "dir", Shell = ScriptShell.Cmd });
        Assert.Equal(("cmd.exe", "/k dir"), (info.FileName, info.Arguments));
    }

    [Fact]
    public void Admin_uses_runas()
    {
        var info = ScriptCommand.Build(new ScriptStep { Source = ScriptSource.Commande, Code = "dir", Shell = ScriptShell.Cmd, Admin = true, WaitForExit = true });
        Assert.Equal(("runas", "/c dir"), (info.Verb, info.Arguments));
    }

    [Theory]
    [InlineData("", "Aucun script n'est choisi.")]
    [InlineData(@"C:\nexistepas\x.ps1", @"Le script « C:\nexistepas\x.ps1 » est introuvable.")]
    public void Missing_files_have_clear_messages(string path, string message) =>
        Assert.Equal(message, Assert.Throws<StepFailedException>(() => ScriptCommand.Build(new ScriptStep { Path = path })).Message);

    [Fact]
    public void Empty_command_has_a_clear_message() =>
        Assert.Equal("La commande est vide.", Assert.Throws<StepFailedException>(() =>
            ScriptCommand.Build(new ScriptStep { Source = ScriptSource.Commande, Code = "  " })).Message);
}
