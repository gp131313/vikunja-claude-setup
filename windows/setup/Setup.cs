// Setup.cs — однофайловый установщик Vikunja Claude Setup. Внутри exe лежат install.ps1 и uninstall.ps1:
// он распаковывает их в папку установки и запускает install.ps1 без окна консоли, передавая токен
// через переменную окружения (в командной строке токен не появляется). Прав администратора не требует.
// Сборка — build.ps1. Один исходник, два файла:
//   Vikunja-Claude-Setup.exe         мастер «Далее → Установить → Готово»
//   Vikunja-Claude-Setup-Silent.exe  без окон: /url=<адрес> /tokenfile=<файл с токеном> [/noshortcut] [/noclaude]
//                                    (то же даёт ключ /silent у первого; токен можно дать и переменной VIKUNJA_MCP_TOKEN)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("Vikunja Claude Setup")]
[assembly: AssemblyProduct("Vikunja Claude Setup")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

public static class Setup
{
    public const string Title = "Vikunja Claude Setup";
    public static bool Ru = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
    public static string T(string ru, string en) { return Ru ? ru : en; }

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    public static string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VikunjaClaudeSetup");
    public static string ConfigPath = null;   // /config=<путь к .claude.json> — для проверок, по умолчанию %USERPROFILE%\.claude.json

    [STAThread]
    static int Main(string[] args)
    {
        bool silent = Path.GetFileNameWithoutExtension(Application.ExecutablePath).IndexOf("silent", StringComparison.OrdinalIgnoreCase) >= 0;
        string url = null, token = Environment.GetEnvironmentVariable("VIKUNJA_MCP_TOKEN"), tokenFile = null;
        bool noShortcut = false, noClaude = false;
        foreach (string a in args)
        {
            string s = a.TrimStart('/', '-');
            string key = s.ToLowerInvariant(), val = null;
            int eq = s.IndexOf('=');
            if (eq > 0) { key = s.Substring(0, eq).ToLowerInvariant(); val = s.Substring(eq + 1).Trim('"'); }
            switch (key)
            {
                case "silent": case "verysilent": case "quiet": case "s": case "q": silent = true; break;
                case "url": url = val; break;
                case "token": token = val; break;
                case "tokenfile": tokenFile = val; break;
                case "noshortcut": noShortcut = true; break;
                case "noclaude": case "noclaudecode": noClaude = true; break;
                case "dir": if (!string.IsNullOrEmpty(val)) Dir = Path.GetFullPath(val).TrimEnd('\\'); break;
                case "config": ConfigPath = val; break;
            }
        }
        if (silent)
        {
            try
            {
                string extra = (noShortcut ? " -NoShortcut" : "") + (noClaude ? " -NoClaudeCode" : "") + (tokenFile != null ? " -TokenFile \"" + tokenFile + "\"" : "");
                string stdout, stderr;
                return RunInstall(url, token, "-Silent" + extra, out stdout, out stderr);
            }
            catch { return 1; }
        }
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Wizard w = new Wizard();
        Application.Run(w);
        return w.ExitCode;
    }

    // распаковать вложенные скрипты в папку установки и выполнить install.ps1; токен уходит через окружение процесса
    public static int RunInstall(string url, string token, string psArgs, out string stdout, out string stderr)
    {
        string dir = Dir;
        Directory.CreateDirectory(dir);
        Assembly asm = Assembly.GetExecutingAssembly();
        foreach (string name in asm.GetManifestResourceNames())
        {
            using (Stream src = asm.GetManifestResourceStream(name))
            using (FileStream dst = File.Create(Path.Combine(dir, name)))
                src.CopyTo(dst);
        }
        string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");
        string argLine = "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(dir, "install.ps1") + "\" -NoFinishBox -Dir \"" + dir + "\"";
        if (!string.IsNullOrEmpty(url)) argLine += " -Url \"" + url.Replace("\"", "") + "\"";
        if (!string.IsNullOrEmpty(ConfigPath)) argLine += " -ConfigPath \"" + ConfigPath + "\"";
        argLine += " " + psArgs;
        ProcessStartInfo psi = new ProcessStartInfo(ps, argLine);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        if (!string.IsNullOrEmpty(token)) psi.EnvironmentVariables["VIKUNJA_MCP_TOKEN"] = token;
        using (Process p = Process.Start(psi))
        {
            stdout = p.StandardOutput.ReadToEnd();
            stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            try { File.Delete(Path.Combine(dir, "install.ps1")); } catch { }
            return p.ExitCode;
        }
    }
}

// Мастер: 0 приветствие → 1 параметры → 2 установка → 3 готово
public class Wizard : Form
{
    public int ExitCode = 1;
    int page;
    string error, resultLine;
    Label head, sub, body;
    Panel optPanel;
    TextBox urlBox, tokenBox;
    CheckBox showBox, claudeBox, shortcutBox;
    ProgressBar bar;
    Button back, next, cancel;
    static string T(string ru, string en) { return Setup.T(ru, en); }
    float scale = 1F;
    int S(int v) { return (int)Math.Round(v * scale); }
    Point P(int x, int y) { return new Point(S(x), S(y)); }
    Size Z(int w, int h) { return new Size(S(w), S(h)); }

    public Wizard()
    {
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None;
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96F;
        ClientSize = Z(520, 380);
        Text = T("Установка — ", "Setup — ") + Setup.Title;
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        Panel header = new Panel(); header.BackColor = Color.White; header.Location = P(0, 0); header.Size = Z(520, 66);
        head = new Label(); head.Font = new Font("Segoe UI", 11F, FontStyle.Bold); head.Location = P(18, 12); head.Size = Z(484, 24);
        sub = new Label(); sub.ForeColor = Color.FromArgb(90, 90, 90); sub.Location = P(18, 38); sub.Size = Z(484, 20);
        header.Controls.Add(head); header.Controls.Add(sub);
        Label line1 = new Label(); line1.BorderStyle = BorderStyle.Fixed3D; line1.Location = P(0, 66); line1.Size = Z(520, 2);

        body = new Label(); body.Location = P(24, 86); body.Size = Z(472, 232);

        optPanel = new Panel(); optPanel.Location = P(24, 82); optPanel.Size = Z(472, 240); optPanel.Visible = false;
        Label urlLabel = new Label(); urlLabel.Text = T("Адрес Vikunja (как в браузере):", "Vikunja address (as in the browser):"); urlLabel.Location = P(0, 0); urlLabel.Size = Z(472, 20);
        urlBox = new TextBox(); urlBox.Location = P(0, 21); urlBox.Size = Z(472, 23);
        Label tokenLabel = new Label(); tokenLabel.Text = T("MCP-токен (Vikunja → Настройки → MCP → «Создать токен»):", "MCP token (Vikunja → Settings → MCP → Create token):"); tokenLabel.Location = P(0, 52); tokenLabel.Size = Z(472, 20);
        tokenBox = new TextBox(); tokenBox.Location = P(0, 73); tokenBox.Size = Z(380, 23); tokenBox.UseSystemPasswordChar = true;
        showBox = new CheckBox(); showBox.Text = T("Показать", "Show"); showBox.Location = P(388, 73); showBox.Size = Z(84, 24);
        showBox.CheckedChanged += delegate { tokenBox.UseSystemPasswordChar = !showBox.Checked; };
        claudeBox = new CheckBox(); claudeBox.Text = T("Подключить к Claude Code на этом компьютере", "Connect Claude Code on this computer"); claudeBox.Checked = true; claudeBox.Location = P(0, 108); claudeBox.Size = Z(472, 24);
        shortcutBox = new CheckBox(); shortcutBox.Text = T("Ярлык веб-приложения «Vikunja» на рабочем столе", "\"Vikunja\" web-app shortcut on the desktop"); shortcutBox.Checked = true; shortcutBox.Location = P(0, 134); shortcutBox.Size = Z(472, 24);
        Label note = new Label(); note.ForeColor = Color.FromArgb(90, 90, 90); note.Location = P(0, 166); note.Size = Z(472, 72);
        note.Text = T("Токен проверяется сразу: установщик обратится к MCP-серверу Vikunja. Токен сохраняется только в вашей "
                    + "конфигурации Claude Code (%USERPROFILE%\\.claude.json) и никуда больше не отправляется.\n"
                    + "Нужна Vikunja 2.7 или новее. Права администратора не нужны.",
                      "The token is verified right away: Setup calls the Vikunja MCP server. The token is stored only in your "
                    + "Claude Code configuration (%USERPROFILE%\\.claude.json) and is not sent anywhere else.\n"
                    + "Vikunja 2.7 or newer is required. No administrator rights are needed.");
        optPanel.Controls.Add(urlLabel); optPanel.Controls.Add(urlBox); optPanel.Controls.Add(tokenLabel); optPanel.Controls.Add(tokenBox); optPanel.Controls.Add(showBox);
        optPanel.Controls.Add(claudeBox); optPanel.Controls.Add(shortcutBox); optPanel.Controls.Add(note);

        bar = new ProgressBar(); bar.Style = ProgressBarStyle.Marquee; bar.MarqueeAnimationSpeed = 30; bar.Location = P(24, 130); bar.Size = Z(472, 18); bar.Visible = false;

        Label line2 = new Label(); line2.BorderStyle = BorderStyle.Fixed3D; line2.Location = P(0, 328); line2.Size = Z(520, 2);
        back = new Button(); back.Text = T("< Назад", "< Back"); back.Location = P(226, 342); back.Size = Z(88, 26);
        next = new Button(); next.Location = P(320, 342); next.Size = Z(88, 26);
        cancel = new Button(); cancel.Text = T("Отмена", "Cancel"); cancel.Location = P(420, 342); cancel.Size = Z(88, 26);
        back.Click += delegate { ShowPage(0); };
        next.Click += delegate { if (page == 0) ShowPage(1); else if (page == 1) StartInstall(); else Close(); };
        cancel.Click += delegate { Close(); };
        CancelButton = cancel;

        Controls.Add(bar); Controls.Add(optPanel); Controls.Add(body);
        Controls.Add(header); Controls.Add(line1); Controls.Add(line2);
        Controls.Add(back); Controls.Add(next); Controls.Add(cancel);
        ResumeLayout(false);
        PerformLayout();

        FormClosing += delegate(object s, FormClosingEventArgs e) { if (page == 2) e.Cancel = true; };
        ShowPage(0);
    }

    public void ShowPage(int p)
    {
        page = p;
        optPanel.Visible = (p == 1);
        body.Visible = (p != 1);
        bar.Visible = (p == 2);
        back.Visible = (p < 2); back.Enabled = (p == 1);
        next.Enabled = (p != 2);
        cancel.Enabled = (p < 2);
        AcceptButton = (p == 1) ? null : next;   // на странице параметров Enter не должен запускать установку случайно
        switch (p)
        {
            case 0:
                head.Text = T("Подключение Claude к Vikunja", "Connect Claude to Vikunja");
                sub.Text = T("Ваши дела — в разговоре с Claude", "Your tasks — in a conversation with Claude");
                body.Text = T("Vikunja 2.7 умеет отдавать задачи AI-ассистенту по протоколу MCP. Этот установщик подключает к вашей Vikunja "
                            + "Claude Code на этом компьютере и ставит ярлык веб-приложения Vikunja.\n\n"
                            + "Понадобятся: адрес вашей Vikunja и MCP-токен, который создаётся в ней на странице «Настройки → MCP» "
                            + "под вашей учётной записью. Claude получит ровно те права, что у этой учётки.\n\n"
                            + "Нажмите «Далее», чтобы продолжить.",
                              "Vikunja 2.7 can expose your tasks to an AI assistant over the MCP protocol. This setup connects Claude Code "
                            + "on this computer to your Vikunja and adds a Vikunja web-app shortcut.\n\n"
                            + "You will need your Vikunja address and an MCP token created in Vikunja on the Settings → MCP page "
                            + "under your own account. Claude gets exactly the permissions of that account.\n\n"
                            + "Click Next to continue.");
                next.Text = T("Далее >", "Next >");
                break;
            case 1:
                head.Text = T("Параметры подключения", "Connection settings");
                sub.Text = T("Введите адрес и токен, затем нажмите «Установить»", "Enter the address and the token, then click Install");
                next.Text = T("Установить", "Install");
                if (string.IsNullOrEmpty(urlBox.Text)) urlBox.Focus(); else tokenBox.Focus();
                break;
            case 2:
                head.Text = T("Проверка и настройка", "Checking and configuring");
                sub.Text = T("Подождите, это займёт несколько секунд", "Please wait, this takes a few seconds");
                body.Text = T("Обращаюсь к MCP-серверу Vikunja и записываю настройки…", "Calling the Vikunja MCP server and writing the settings…");
                break;
            case 3:
                head.Text = T("Готово", "Done");
                sub.Text = T("Vikunja подключена", "Vikunja is connected");
                body.Text = DoneText();
                next.Text = T("Готово", "Finish");
                next.Focus();
                break;
        }
    }

    string DoneText()
    {
        string tools = "";
        if (resultLine != null)
        {
            int i = resultLine.IndexOf("tools=");
            if (i >= 0) { string n = resultLine.Substring(i + 6).Split(' ')[0]; if (n != "-1") tools = T(" Инструментов MCP: ", " MCP tools: ") + n + "."; }
        }
        string mcpUrl = urlBox.Text.Trim().TrimEnd('/');
        if (!mcpUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) mcpUrl = "https://" + mcpUrl;
        mcpUrl += "/api/v2/mcp";
        StringBuilder sb = new StringBuilder();
        sb.Append(T("Токен принят, MCP-сервер Vikunja отвечает.", "The token was accepted; the Vikunja MCP server responds.")).Append(tools).Append("\n\n");
        if (claudeBox.Checked) sb.Append(T("Claude Code: сервер «vikunja» записан в конфигурацию и появится в следующей сессии Claude Code.\n\n",
                                           "Claude Code: the \"vikunja\" server is in your configuration and will appear in the next Claude Code session.\n\n"));
        if (shortcutBox.Checked) sb.Append(T("Ярлык «Vikunja» — на рабочем столе и в меню «Пуск».\n\n", "The \"Vikunja\" shortcut is on the desktop and in the Start menu.\n\n"));
        sb.Append(T("Для обычного Claude (сайт, приложение, телефон) добавьте пользовательский коннектор в настройках claude.ai: "
                  + "URL " + mcpUrl + ", Authentication — «No sign-in», заголовок Authorization = «Bearer <токен>».\n\n",
                    "For regular Claude (web, desktop, phone) add a custom connector in claude.ai settings: "
                  + "URL " + mcpUrl + ", Authentication — \"No sign-in\", header Authorization = \"Bearer <token>\".\n\n"));
        sb.Append(T("Удаление: «Параметры» → «Приложения» → ", "To uninstall: Settings → Apps → ")).Append(Setup.Title).Append(".");
        return sb.ToString();
    }

    void StartInstall()
    {
        string url = urlBox.Text.Trim(), token = tokenBox.Text.Trim();
        if (url.Length == 0) { MessageBox.Show(this, T("Введите адрес Vikunja.", "Enter the Vikunja address."), Setup.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning); urlBox.Focus(); return; }
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token.Substring(7).Trim();
        if (token.Length == 0) { MessageBox.Show(this, T("Введите MCP-токен.", "Enter the MCP token."), Setup.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning); tokenBox.Focus(); return; }
        ShowPage(2);
        string psArgs = (claudeBox.Checked ? "" : " -NoClaudeCode") + (shortcutBox.Checked ? "" : " -NoShortcut");
        Task.Factory.StartNew<int>(delegate
        {
            try
            {
                string so, se;
                int code = Setup.RunInstall(url, token, psArgs, out so, out se);
                resultLine = so; error = se.Trim();
                return code;
            }
            catch (Exception ex) { error = ex.Message; return 1; }
        }).ContinueWith(delegate(Task<int> t) { Done(t.Result); }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void Done(int code)
    {
        ExitCode = code;
        if (code != 0)
        {
            string msg;
            switch (code)
            {
                case 2: msg = T("Vikunja не приняла токен.", "Vikunja did not accept the token."); break;
                case 3: msg = T("Адрес недоступен.", "The address is unreachable."); break;
                case 4: msg = T("По этому адресу нет MCP-сервера Vikunja.", "There is no Vikunja MCP server at this address."); break;
                default: msg = T("Не удалось выполнить настройку.", "Setup could not complete."); break;
            }
            if (!string.IsNullOrEmpty(error)) msg += "\n\n" + error;
            MessageBox.Show(this, msg, Setup.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            ShowPage(1);   // вернуться к параметрам, чтобы поправить и повторить
            return;
        }
        ShowPage(3);
        Activate();
    }
}
