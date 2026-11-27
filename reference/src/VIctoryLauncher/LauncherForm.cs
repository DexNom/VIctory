using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.VisualBasic.Devices;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace VIctoryLauncher;

internal class LauncherForm : Form
{
	private class Field : Panel
	{
		public Field()
		{
			DoubleBuffered = true;
			BackColor = Theme.Back;
			ResizeRedraw = true;
		}
	}

	private class Step
	{
		public string Title;

		public string Why;

		public string FixText;

		public Func<int> Check;

		public Func<bool> Fix;

		public bool Auto = true;

		public Func<bool> Applies;

		public StatusMark Badge;

		public StatusMark Chip;

		public int NodeY;

		public Label Text;

		public Label Detail;

		public Pill Button;

		public int State = -2;
	}

	private class HardwareInfo
	{
		public string Id = "";

		public string Gpu = "";

		public string GpuVendor = "";

		public string Driver = "";

		public string Cpu = "";

		public string Board = "";

		public string BoardMaker = "";

		public string Os = "";

		public string Disk = "";

		public int VramMb;

		public int RamGb;

		public int Cores;

		public int Threads;

		public bool Laptop;

		public bool RebarBios;

		public int RebarHeap = -1;
	}

	private const int PagePlay = 0;

	private const int PagePerf = 1;

	private const int PageShaders = 2;

	private const int PageControls = 3;

	private const int PageSetup = 4;

	private const int PageHardware = 5;

	private const int PageUpdates = 6;

	private const int PageHelp = 7;

	private const int PageCustom = 8;

	private const int ModeUltraLow = 5;

	private const int ModeCustom = 6;

	public const string DiscordInvite = "https://discord.gg/648WEXCAsZ";

	public const string DiscordChannel = "Send bugs here please";

	public const string RepoUrl = "https://github.com/felicienradio/cold-project-vi/tree/main";

	private const int HeroButtonsY = 256;

	private const string TitleId = "7952987A";

	private const string GalleryUrl = "https://vi-hardware.edupodept.workers.dev/saves";

	private const string GalleryEndpoint = "https://vi-hardware.edupodept.workers.dev/v1/saves";

	private const int MaxSaveUpload = 1048576;

	private const string SaveHashes = "vi-save.sha256";

	private const long MaxSaveFile = 33554432L;

	private const long MaxSaveTotal = 100663296L;

	private const int MaxSaveFiles = 400;

	private const int AutoBackupsKept = 15;

	private const double GameCommitGb = 24.0;

	public const string AmdRepo = "https://github.com/Belobug/VIctory-Belobug-AMD";

	public const string AmdBranch = "a-publier";

	public const string AmdCommit = "4eb6cfdf9e61cb112f1f811344ec0dd309eddd6c";

	public const string HardwareEndpoint = "https://vi-hardware.edupodept.workers.dev/v1/hw";

	public const string ReportEndpoint = "https://vi-hardware.edupodept.workers.dev/v1/report";

	private const int MaxUpload = 1843200;

	private const int SessionTail = 262144;

	public const string SafetyUrl = "https://github.com/felicienradio/cold-project-vi/blob/main/docs/SAFETY.md";

	public const string StatsJsonUrl = "https://raw.githubusercontent.com/felicienradio/cold-project-vi/main/docs/hardware-stats.json";

	public const string StatsPageUrl = "https://github.com/felicienradio/cold-project-vi/blob/main/docs/HARDWARE-STATS.md";

	public const string AgilityUrl = "https://github.com/felicienradio/cold-project-vi/releases/download/v0.3.7/D3D12Core.dll";

	public const string AgilitySha256 = "70a6d47d335b4a15180544c01a35b84474522a369dc582d85fe057313a3aeaa0";

	private static bool Pt;

	private static bool Fr;

	private readonly string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

	private readonly string dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VIctory", "luz");

	private readonly string settingsFile;

	private string cacheDir;

	private string dataDir;

	private Hero banner;

	private FootMeta footMeta;

	private StatusStrip statusStrip;

	private TextBox gameBox;

	private Label gameStatus;

	private Label buildStatus;

	private Label modeInfo;

	private Label vramSummary;

	private Label smoothStatus;

	private Label warnText;

	private Label updateBadge;

	private Panel warnPanel;

	private Pill warnFix;

	private Heading presetHeading;

	private Heading gameHeading;

	private readonly List<ModeCard> cards = new List<ModeCard>();

	private int modeIndex = -1;

	private CheckBox panelBox;

	private CheckBox borderlessBox;

	private CheckBox fps60Box;

	private CheckBox reserveBox;

	private CheckBox smoothBox;

	private CheckBox qualityBox;

	private CheckBox facesBox;

	private CheckBox loadingBox;

	private CheckBox fightsBox;

	private CheckBox warmBox;

	private CheckBox cpuBox;

	private LoadBar loadBar;

	private TextBox logBox;

	private Pill playButton;

	private Pill prepareButton;

	private Pill buildButton;

	private Pill closeButton;

	private ListView vramList;

	private readonly List<Panel> pages = new List<Panel>();

	private readonly List<NavButton> nav = new List<NavButton>();

	private bool rebar;

	private bool rtxFrameGen;

	private bool canSmooth;

	private bool rebarBios;

	private bool barRead;

	private bool probeOptions16;

	private int rebarHeap = -1;

	private string probeAdapter = "";

	private string probeVendor = "";

	private double vramGb;

	private double ramGb;

	private string gpuName = "unknown GPU";

	private string smoothState = "?";

	private Mode[] modes;

	private static readonly int[] CardOrder = new int[7] { 0, 1, 2, 5, 3, 4, 6 };

	private ToolTip tips;

	private int shownPage = -1;

	private Pill heroGhost;

	private Pill heroAuto;

	private Panel footPanel;

	private Action layoutFoot;

	private Field logPanel;

	private Label explain;

	private Label vramEmpty;

	private static readonly HashSet<string> ProtectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"dwm", "csrss", "explorer", "System", "Idle", "svchost", "winlogon", "services", "lsass", "smss",
		"wininit", "fontdrvhost", "sihost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "TextInputHost", "ctfmon", "RuntimeBroker", "audiodg",
		"Memory Compression", "Registry", "victory", "victory-ferramenta", "edu-launcher", "Edu's Remake", "Julia's Launcher", "MsMpEng", "NVDisplay.Container", "nvcontainer",
		"claude", "msedgewebview2", "conhost", "powershell", "pwsh", "cmd", "WindowsTerminal", "OpenConsole", "ApplicationFrameHost", "LockApp",
		"SecurityHealthSystray", "Taskmgr", "PredatorSense", "Secure System", "LsaIso", "spoolsv", "dllhost"
	};

	private ComboBox outputBox;

	private Action showEnginePills;

	private bool busyNow;

	private volatile bool closing;

	private volatile bool closePrompt;

	private static readonly object settingsLock = new object();

	private static readonly Regex SavePath = new Regex("^[0-9A-Fa-f]{16}/[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}/[A-Za-z0-9_]{1,64}~[0-9A-Fa-f]{16}/v[0-9]{1,5}/([0-9]{1,4}|manifest)$");

	private readonly List<Step> steps = new List<Step>();

	private LoadBar setupBar;

	private Pill everything;

	private volatile bool setupBusy;

	private int everyW = 236;

	private int timelineFirstY;

	private TimelineLine tline;

	private PageHead setupHead;

	private Control setupAnchor;

	private float setupProgress;

	private Control loadBarAnchor;

	private string sourceSeen;

	private static readonly string[] DumpFiles = new string[2] { "shaders.rpf", "levels.rpf" };

	private Process gameProc;

	private Process afterWarm;

	private DateTime? windowGone;

	private bool gameHadWindow;

	private readonly List<Process> tools = new List<Process>();

	private bool pipelineHintShown;

	private bool heavyAppsAsked;

	private static readonly string[] HeavyApps = new string[13]
	{
		"firefox", "chrome", "msedge", "opera", "brave", "vivaldi", "Discord", "Spotify", "Teams", "ms-teams",
		"slack", "steamwebhelper", "EpicGamesLauncher"
	};

	private volatile string exeProblem;

	private volatile int shaderFiles;

	private volatile bool pagefileOk = true;

	private volatile bool powerOk = true;

	private volatile bool gpuPrefOk = true;

	private volatile bool shadersOk = true;

	private bool warnedThisSession;

	private ComboBox exeBox;

	private string exeChoice;

	private string exesOf;

	private bool fillingExes;

	private bool hasNvidia;

	private bool hasRadeon;

	private bool rtx30;

	private List<GpuAdapter> adapters = new List<GpuAdapter>();

	private bool integratedOnly;

	private string driverVersion = "";

	private string radeonName = "";

	private ComboBox fgBox;

	private readonly List<string> fgKeys = new List<string>();

	private HardwareInfo hw;

	private Label idLabel;

	private InfoCard hwText;

	private TextBox hwJson;

	private CheckBox shareBox;

	private Label shareStatus;

	private ComboBox engineBox;

	private Manifest pending;

	private string pendingDir;

	private DateTime lastCheck;

	private string updateState = "";

	private Label updVersion;

	private Label updState;

	private Label updLast;

	private TextBox updNotes;

	private CheckBox autoUpdateBox;

	private Pill installNow;

	private System.Threading.Timer updateTimer;

	private int checking;

	private string installedVersion;

	private bool installing;

	private CustomProfile custom = new CustomProfile();

	private ComboBox cScale;

	private ComboBox cTex;

	private ComboBox cBuf;

	private ComboBox cRipe;

	private ComboBox cStage;

	private ComboBox cFps;

	private CheckBox cGuards;

	private CheckBox cLevels;

	private TextBox pasteBox;

	private TextBox codeBox;

	private bool fillingCustom;

	private StatusMark[] shaderMarks;

	private Label[] shaderTexts;

	private TextBox shaderErrors;

	private LoadBar shaderBar;

	private LoadBar pipeBar;

	private ToolTip shaderTips = new ToolTip
	{
		AutoPopDelay = 20000
	};

	private readonly bool[] stageLive = new bool[2];

	private System.Windows.Forms.Timer shaderTimer;

	private volatile bool shaderReading;

	private OptionCard logsBox;

	private static readonly string[] NewEn = new string[75]
	{
		"IN 0.3.16", "  •  The sun is white again: it showed as a black disc in the sky. The rest of the picture stays as it is.", "  •  Pick your frame generation on Play: Automatic, NVIDIA Smooth Motion (RTX 40/50), NVSmooth30 (RTX 30)", "     or VIctory 2X (any card, experimental). The F3 panel says which one runs, and no longer cuts its last line.", "  •  When Play is greyed out, the button says why (setup still working, no game chosen, no engine).", "  •  Setup no longer stays 'busy' when one of its steps fails.", "  •  Rust and the C++ Build Tools are no longer asked for when nothing has to be built (they were asked", "     again after an update even when installed). Playing never needs them.", "  •  Shaders are prepared once more (the translator changed for the sun).", "",
		"IN 0.3.15", "  •  A new look by Cyanexani.", "", "IN 0.3.14", "  •  The picture is back as it was in 0.3.11: since 0.3.12 the whole screen could turn cyan and dark. The engine is", "     0.3.11's again (the black sun and the fixes of 0.3.13 come back once tested). Shaders prepared for 0.3.11 are", "     used again: nothing to prepare.", "  •  'Anti-stretch' is gone: it broke the picture and did not help.", "", "IN 0.3.13",
		"  •  Fixes from the NVIDIA fork (VIctory-NVIDIA, thanks to its authors): violet blotches from the ambient light", "     probes, wrong colours in some compressed textures (BC7), descriptors given back to the wrong heap, buffer", "     views reaching past their buffer (a cause of the card stopping), helper threads at the right priority.", "", "IN 0.3.12", "  •  The sun is no longer a black disc: a colour too bright to hold is kept at the brightest value instead of", "     made black. The game translates its shaders again once after this update (Shaders > Prepare, ~5 min).", "", "IN 0.3.11", "  •  Fewer crashes on 16 GB PCs: when Windows runs out of memory for a moment, VIctory lets go of the",
		"     memory it keeps in reserve and tries again, instead of closing (seen on RX 9070 XT, RX 6600, RTX 4070).", "  •  Your saves are copied before every game (the newest 15 are kept): Hardware & ID > Game saves >", "     'Restore a backup...' brings any of them back.", "", "IN 0.3.9", "  •  Rainbow textures: the fix announced in 0.3.7 was left out of that build by mistake. It is in now.", "  •  '720p' no longer closes the game at start. It is now 'World at 720p (lighter)': the screen stays 1080p", "     and the world is drawn at 720p at most (the game itself refuses a 720p screen).", "  •  'Resizable BAR in the NVIDIA driver' now works: Windows asks for administrator rights once.", "",
		"IN 0.3.7", "  •  Rainbow textures: a texture that had not been read yet showed another texture's leftover bytes", "     (faces, shirts, walls full of coloured noise). Now it waits, empty, for a moment instead.", "  •  Windows 10: the game starts. The D3D12 Agility SDK (Microsoft's) now comes with VIctory.", "  •  Your fastest drive: Shaders > 'Move to another drive...' moves the shader cache, the game's data and", "     saves, the AMD engine and builds from source to any drive.", "", "IN 0.3.6", "NEW", "  •  Output 720p: pick 720p or 1080p (or 2K, 4K) next to the presets. At 720p, Recommended draws",
		"     native 720p and the 540p presets are upscaled to 720p instead of 1080p: a sharper picture for less.", "  •  Game logs (you choose): when the game closes, a standard log of that game can go to edupode, so", "     the next optimizations come from real PCs. Asked once; change it on Hardware & ID.", "  •  This window: what changed, every time the launcher is installed or updated.", "  •  Controls page by what you are doing: on foot, talking, fighting, driving, flying, menus.", "  •  Hardware & ID: the reference PC the presets are measured on, and the players' PCs (a table and charts,", "     updated every 6 hours) to compare yours with.", "  •  Older copies of the launcher (Downloads, Desktop) can go to the Recycle Bin; your settings stay.", "", "BETTER",
		"  •  Resizable BAR: no more 'Fix it' on cards that cannot have it (GTX, RTX 20), on Windows 10 (the BIOS", "     has it on, Windows does not offer it to games) or before the check has finished.", "  •  Shaders: 'Shaders prepared' no longer turns red while the game or the warm-up writes the cache.", "  •  Graphics cards: each card listed once (it showed up to four times), virtual displays left out.", "  •  Buttons: long labels fit, keyboard focus is shown, and buttons react when pressed.", "", "REMOVED", "  •  The launcher no longer changes antivirus settings, and no longer runs hidden PowerShell commands.", "     Windows settings (power, virtual memory) are now set by the launcher itself.", "  •  The deadly virus was removed from the game. (Like Herobrine, it was never there.)",
		"", "IS IT SAFE?", "  Yes. Every file is built from the open source code by GitHub Actions, with a provenance attestation", "  and SHA-256 sums on the release page, and every update is signed. Windows warns about programs that", "  are new and not code-signed (a certificate costs money): Help > 'Is it safe?' shows how to check."
	};

	private static readonly string[] NewPt = new string[75]
	{
		"NA 0.3.16", "  •  O sol volta a ser branco: aparecia como um disco preto no ceu. O resto da imagem fica como esta.", "  •  Escolhe a geracao de frames em Jogar: Automatico, NVIDIA Smooth Motion (RTX 40/50), NVSmooth30 (RTX 30)", "     ou VIctory 2X (qualquer placa, experimental). O painel F3 diz qual corre e ja nao corta a ultima linha.", "  •  Quando Jogar esta cinzento, o botao diz porque (o Preparar ainda a trabalhar, sem jogo, sem motor).", "  •  O Preparar ja nao fica 'ocupado' quando um dos passos falha.", "  •  O Rust e o C++ Build Tools ja nao sao pedidos quando nao ha nada para compilar (eram pedidos outra", "     vez depois de um update, mesmo instalados). Para jogar nunca sao precisos.", "  •  Os shaders sao preparados mais uma vez (o tradutor mudou por causa do sol).", "",
		"NA 0.3.15", "  •  Um visual novo do Cyanexani.", "", "NA 0.3.14", "  •  A imagem volta a ser a da 0.3.11: desde a 0.3.12 o ecra todo podia ficar verde-azulado e escuro. O motor e", "     de novo o da 0.3.11 (o sol preto e as correcoes da 0.3.13 voltam depois de testadas). Os shaders preparados", "     para a 0.3.11 voltam a ser usados: nada a preparar.", "  •  O 'Anti-esticados' saiu: estragava a imagem e nao ajudava.", "", "NA 0.3.13",
		"  •  Correcoes do fork NVIDIA (VIctory-NVIDIA, obrigado aos autores): manchas violeta da luz ambiente, cores", "     erradas em algumas texturas comprimidas (BC7), descritores devolvidos ao heap errado, vistas de buffer para", "     la do buffer (uma causa de a placa parar), threads auxiliares na prioridade certa.", "", "NA 0.3.12", "  •  O sol deixa de ser um disco preto: uma cor demasiado brilhante fica no valor mais brilhante em vez de", "     ficar preta. O jogo traduz os shaders de novo uma vez depois deste update (Shaders > Preparar, ~5 min).", "", "NA 0.3.11", "  •  Menos crashes em PCs de 16 GB: quando o Windows fica sem memoria por um momento, o VIctory liberta a",
		"     memoria que guarda de reserva e tenta de novo, em vez de fechar (visto em RX 9070 XT, RX 6600, RTX 4070).", "  •  Os teus saves sao copiados antes de cada jogo (ficam os 15 mais recentes): Hardware e ID > Saves do jogo >", "     'Restaurar uma copia...' traz qualquer um de volta.", "", "NA 0.3.9", "  •  Texturas arco-iris: a correcao anunciada na 0.3.7 ficou de fora dessa versao por engano. Agora esta.", "  •  O '720p' ja nao fecha o jogo ao arrancar. Passa a ser 'Mundo a 720p (mais leve)': o ecra fica a 1080p", "     e o mundo e desenhado a 720p no maximo (o proprio jogo recusa um ecra de 720p).", "  •  'Resizable BAR no driver da NVIDIA' funciona: o Windows pede permissoes de administrador uma vez.", "",
		"NA 0.3.7", "  •  Texturas arco-iris: uma textura ainda nao lida mostrava os restos de outra (caras, camisolas,", "     paredes cheias de ruido colorido). Agora fica vazia um instante em vez disso.", "  •  Windows 10: o jogo arranca. O D3D12 Agility SDK (da Microsoft) vem agora com o VIctory.", "  •  O teu disco mais rapido: Shaders > 'Mudar para outro disco...' muda a cache de shaders, os dados e", "     saves do jogo, o motor AMD e as compilacoes para qualquer disco.", "", "NA 0.3.6", "NOVO", "  •  Saida 720p: escolhe 720p ou 1080p (ou 2K, 4K) ao lado das predefinicoes. Em 720p, o Recomendado",
		"     desenha 720p nativo e as predefinicoes de 540p sao ampliadas para 720p em vez de 1080p: imagem mais nitida.", "  •  Logs do jogo (tu escolhes): quando o jogo fecha, uma log padrao desse jogo pode ir para o edupode,", "     para as proximas otimizacoes virem de PCs reais. Perguntado uma vez; muda em Hardware e ID.", "  •  Esta janela: o que mudou, sempre que o launcher e instalado ou atualizado.", "  •  Pagina de controlos pelo que estas a fazer: a pe, falar, lutar, conduzir, voar, menus.", "  •  Hardware e ID: o PC de referencia onde as predefinicoes sao medidas, e os PCs dos jogadores (tabela e", "     graficos, atualizados de 6 em 6 horas) para comparares o teu.", "  •  Copias antigas do launcher (Transferencias, Ambiente de trabalho) podem ir para a Reciclagem; as definicoes ficam.", "", "MELHOR",
		"  •  Resizable BAR: acabou o 'Corrigir' em placas que nao o podem ter (GTX, RTX 20), no Windows 10 (a BIOS", "     tem-no ligado, o Windows nao o oferece aos jogos) ou antes de a verificacao acabar.", "  •  Shaders: 'Shaders preparados' ja nao fica vermelho enquanto o jogo ou o aquecimento escrevem a cache.", "  •  Placas graficas: cada placa aparece uma vez (aparecia ate quatro), ecras virtuais ficam de fora.", "  •  Botoes: textos longos cabem, o foco do teclado aparece e os botoes reagem ao clique.", "", "REMOVIDO", "  •  O launcher ja nao muda definicoes do antivirus nem corre comandos PowerShell escondidos.", "     As definicoes do Windows (energia, memoria virtual) sao feitas pelo proprio launcher.", "  •  O virus mortal foi removido do jogo. (Como o Herobrine, nunca la esteve.)",
		"", "E SEGURO?", "  Sim. Cada ficheiro e compilado a partir do codigo aberto pelo GitHub Actions, com atestado de origem e", "  somas SHA-256 na pagina da release, e cada update e assinado. O Windows avisa sobre programas novos e", "  sem assinatura de codigo (um certificado custa dinheiro): Ajuda > 'E seguro?' mostra como verificar."
	};

	private static readonly string[] NewFr = new string[75]
	{
		"DANS LA 0.3.16", "  •  Le soleil est de nouveau blanc : il apparaissait comme un disque noir. Le reste de l'image ne change pas.", "  •  Choisis ta génération d'images dans Jouer : Automatique, NVIDIA Smooth Motion (RTX 40/50), NVSmooth30", "     (RTX 30) ou VIctory 2X (toute carte, expérimental). Le panneau F3 dit laquelle tourne, sans couper sa dernière ligne.", "  •  Quand Jouer est grisé, le bouton dit pourquoi (installation en cours, pas de jeu, pas de moteur).", "  •  L'installation ne reste plus « occupée » quand une de ses étapes échoue.", "  •  Rust et les C++ Build Tools ne sont plus demandés quand rien n'est à compiler (ils l'étaient à nouveau", "     après une mise à jour, même installés). Pour jouer, ils ne sont jamais nécessaires.", "  •  Les shaders sont préparés une fois de plus (le traducteur a changé pour le soleil).", "",
		"DANS LA 0.3.15", "  •  Une nouvelle apparence signée Cyanexani.", "", "DANS LA 0.3.14", "  •  L'image redevient celle de la 0.3.11 : depuis la 0.3.12 tout l'écran pouvait devenir cyan et sombre. Le moteur", "     est de nouveau celui de la 0.3.11 (le soleil noir et les correctifs de la 0.3.13 reviendront une fois testés).", "     Les shaders préparés pour la 0.3.11 servent à nouveau : rien à préparer.", "  •  'Anti-étirement' a disparu : il cassait l'image sans aider.", "", "DANS LA 0.3.13",
		"  •  Correctifs du fork NVIDIA (VIctory-NVIDIA, merci à ses auteurs) : taches violettes de la lumière ambiante,", "     couleurs fausses dans certaines textures compressées (BC7), descripteurs rendus au mauvais tas, vues de", "     tampon débordant de leur tampon (une cause d'arrêt de la carte), threads d'aide à la bonne priorité.", "", "DANS LA 0.3.12", "  •  Le soleil n'est plus un disque noir : une couleur trop lumineuse est gardée à la valeur la plus claire au lieu", "     de devenir noire. Le jeu retraduit ses shaders une fois après cette mise à jour (Shaders > Préparer, ~5 min).", "", "DANS LA 0.3.11", "  •  Moins de plantages sur les PC de 16 Go : quand Windows manque de mémoire un instant, VIctory libère la",
		"     mémoire qu'il garde en réserve et réessaie, au lieu de se fermer (vu sur RX 9070 XT, RX 6600, RTX 4070).", "  •  Tes sauvegardes sont copiées avant chaque partie (les 15 plus récentes sont gardées) : Matériel et ID >", "     Sauvegardes du jeu > 'Restaurer une copie...' en ramène n'importe laquelle.", "", "DANS LA 0.3.9", "  •  Textures arc-en-ciel : le correctif annoncé dans la 0.3.7 avait été oublié dans cette version. Il y est.", "  •  Le '720p' ne ferme plus le jeu au démarrage. Il devient 'Monde en 720p (plus léger)' : l'écran reste en", "     1080p et le monde est dessiné en 720p au plus (le jeu lui-même refuse un écran 720p).", "  •  'Resizable BAR dans le pilote NVIDIA' fonctionne : Windows demande une fois les droits administrateur.", "",
		"DANS LA 0.3.7", "  •  Textures arc-en-ciel : une texture pas encore lue montrait les restes d'une autre (visages, t-shirts,", "     murs pleins de bruit coloré). Elle reste maintenant vide un instant à la place.", "  •  Windows 10 : le jeu démarre. Le D3D12 Agility SDK (de Microsoft) est fourni avec VIctory.", "  •  Ton disque le plus rapide : Shaders > 'Déplacer vers un autre disque...' déplace le cache de shaders,", "     les données et sauvegardes, le moteur AMD et les compilations vers n'importe quel disque.", "", "DANS LA 0.3.6", "NOUVEAU", "  •  Sortie 720p : choisis 720p ou 1080p (ou 2K, 4K) à côté des préréglages. En 720p, Recommandé dessine",
		"     en 720p natif et les préréglages 540p sont agrandis en 720p au lieu de 1080p : une image plus nette.", "  •  Logs du jeu (au choix) : à la fermeture du jeu, un log standard de la partie peut partir chez edupode,", "     pour que les prochaines optimisations viennent de vrais PC. Demandé une fois ; modifiable dans Matériel et ID.", "  •  Cette fenêtre : ce qui a changé, à chaque installation ou mise à jour du launcher.", "  •  Page Commandes selon ce que tu fais : à pied, parler, combat, conduite, vol, menus.", "  •  Matériel et ID : le PC de référence des préréglages, et les PC des joueurs (tableau et graphiques,", "     mis à jour toutes les 6 heures) pour comparer le tien.", "  •  Les anciennes copies du launcher (Téléchargements, Bureau) peuvent aller à la Corbeille ; tes réglages restent.", "", "MIEUX",
		"  •  Resizable BAR : plus de 'Corriger' sur les cartes qui ne l'ont pas (GTX, RTX 20), sous Windows 10 (activé", "     dans le BIOS, mais Windows ne le propose pas aux jeux) ni avant la fin de la vérification.", "  •  Shaders : 'Shaders préparés' ne passe plus au rouge pendant que le jeu ou le préchauffage écrit le cache.", "  •  Cartes graphiques : chaque carte une seule fois (jusqu'à quatre avant), écrans virtuels exclus.", "  •  Boutons : les textes longs tiennent, le focus clavier se voit, les boutons réagissent au clic.", "", "RETIRÉ", "  •  Le launcher ne modifie plus les réglages de l'antivirus et ne lance plus de commandes PowerShell cachées.", "     Les réglages Windows (alimentation, mémoire virtuelle) sont faits par le launcher lui-même.", "  •  Le virus mortel a été retiré du jeu. (Comme Herobrine, il n'a jamais existé.)",
		"", "EST-CE SÛR ?", "  Oui. Chaque fichier est compilé depuis le code open source par GitHub Actions, avec une attestation", "  d'origine et des sommes SHA-256 sur la page de la release, et chaque mise à jour est signée. Windows met", "  en garde contre les programmes récents sans signature de code : Aide > 'Est-ce sûr ?' montre comment vérifier."
	};

	private Label storageLabel;

	private static readonly object agilityLock = new object();

	private static readonly Dictionary<string, string> FrText = new Dictionary<string, string>
	{
		{ "       On but not offered: Windows Update (to 24H2+) and the newest NVIDIA/AMD driver, then restart.", "       Activé mais pas proposé : Windows Update (vers 24H2+) et le dernier pilote NVIDIA/AMD, puis redémarre." },
		{ "       Resizable BAR firmware update (button below; from your card maker's site for some models).", "       mise à jour du firmware Resizable BAR de NVIDIA (bouton ci-dessous ; sur le site du fabricant pour certains modèles)." },
		{ "       Save and exit (usually F10). Back in Windows, press 'Check again'.", "       Enregistre et quitte (souvent F10). De retour dans Windows, appuie sur 'Vérifier à nouveau'." },
		{ "      without Resizable BAR the copies to the card are slower, so less to copy helps most.", "      sans Resizable BAR les copies vers la carte sont plus lentes, donc copier moins aide le plus." },
		{ "   (no error lines; the whole log: ", "   (aucune ligne d'erreur ; le log complet : " },
		{ "   •  'I can't turn it on' stops the launcher warning you about it.", "   •  'Je ne peux pas l'activer' arrête les avertissements du launcher." },
		{ "   •  Pick Modest PC or Ultra Low, keep 'Keep the most video memory' on, and close apps on the Performance page:", "   •  Choisis PC modeste ou Ultra bas, laisse 'Garder le max de VRAM' activé et ferme des applis dans la page Performances :" },
		{ "   •  The game still works: the launcher leaves the direct video-memory writes off on its own.", "   •  Le jeu marche quand même : le launcher laisse l'écriture directe en VRAM désactivée tout seul." },
		{ "  (Defender: see Help)", "  (Defender : voir l'Aide)" },
		{ "  (Repair tries them again)", "  (Réparer les réessaie)" },
		{ "  ·  laptop", "  ·  portable" },
		{ "  ·  next in 5 minutes", "  ·  prochaine dans 5 minutes" },
		{ " branch", " (branche)" },
		{ " cores, ", " cœurs, " },
		{ "(newer boards: the 'Smart Access Memory / Re-Size BAR' button on the EZ Mode page). Turn CSM off under Boot.", "(cartes récentes : le bouton 'Smart Access Memory / Re-Size BAR' de la page EZ Mode). Désactive le CSM dans Boot." },
		{ "(no errors)", "(aucune erreur)" },
		{ ". Update its BIOS first, then in the BIOS:", ". Mets d'abord son BIOS à jour, puis dans le BIOS :" },
		{ ": built.", " : compilé." },
		{ "\r\n\r\nApply it and play with Custom?", "\r\n\r\nL'appliquer et jouer en Personnalisé ?" },
		{ "\r\n\r\nClose the game now?", "\r\n\r\nFermer le jeu maintenant ?" },
		{ "\r\n\r\nHelp > Send errors to Discord makes a package with it.", "\r\n\r\nAide > Envoyer les erreurs sur Discord en fait un paquet." },
		{ "\r\n\r\nYes: play anyway.   No: open Setup to fix them.", "\r\n\r\nOui : jouer quand même.   Non : ouvrir Installation pour corriger." },
		{ "\r\nNeeds an RTX 40 or 50 series card and driver 571.86 or newer.", "\r\nNécessite une carte RTX 40 ou 50 et le pilote 571.86 ou plus récent." },
		{ "{0:N0} shaders cached", "{0:N0} shaders en cache" },
		{ "{0} cannot use Resizable BAR (only RTX 30 and newer, RX 5000 and newer).", "{0} ne gère pas le Resizable BAR (seulement RTX 30 et plus récentes, RX 5000 et plus récentes)." },
		{ "{0} could not be downloaded: {1}", "Impossible de télécharger {0} : {1}" },
		{ "{0} is not a working program ({1:N0} bytes): the dump is incomplete or still encrypted. Download or extract it again.", "{0} n'est pas un programme valide ({1:N0} octets) : le dump est incomplet ou encore chiffré. Télécharge-le ou extrais-le à nouveau." },
		{ "{0} pipelines loaded.", "{0} pipelines chargées." },
		{ "{0} supports it.", "{0} le gère." },
		{ "{0} to fix", "{0} à corriger" },
		{ "{0}: early RTX 30 cards need NVIDIA's Resizable BAR firmware update (see 2).", "{0} : les premières RTX 30 ont besoin de la mise à jour firmware Resizable BAR de NVIDIA (voir 2)." },
		{ "{0}×{1} internal upscaled to 1080p", "{0}×{1} interne agrandi en 1080p" },
		{ "▶  PLAY", "▶  JOUER" },
		{ "★ A window without borders over the whole screen (test A: 22-24 FPS on edupode's PC). Needed for Smooth Motion and quick Alt+Tab.", "★ Une fenêtre sans bordure sur tout l'écran (test A : 22-24 FPS sur le PC d'edupode). Nécessaire pour Smooth Motion et un Alt+Tab rapide." },
		{ "★ Borderless Fullscreen", "★ Plein écran sans bordure" },
		{ "★ Crowds and fights", "★ Foules et combats" },
		{ "★ Diagnostics Overlay (F3)", "★ Overlay de diagnostic (F3)" },
		{ "★ Recommended setup", "★ Réglages recommandés" },
		{ "★ Shows FPS (now, last-minute average, whole-run average, worst), frame time, draws, video memory and RAM in the upper left corner. F3 hides it.", "★ Affiche les FPS (actuels, moyenne de la dernière minute, moyenne totale, pire), le temps par image, les draws, la VRAM et la RAM en haut à gauche. F3 le masque." },
		{ "★ The game asks for 30 FPS; this lets its clock run up to 60, so cutscenes and menus go past 30 when the PC can.", "★ Le jeu demande 30 FPS ; ceci laisse son horloge monter à 60, donc cinématiques et menus dépassent 30 quand le PC peut." },
		{ "★ Unlock 60 FPS Clock", "★ Débloquer l'horloge à 60 FPS" },
		{ "✓  Game found.", "✓  Jeu trouvé." },
		{ "✓  Game found: {0} executables, choose one on the right.", "✓  Jeu trouvé : {0} exécutables, choisis-en un à droite." },
		{ "1.  Motherboard: ", "1.  Carte mère : " },
		{ "12 GB+ GPUs", "Cartes 12 Go+" },
		{ "2 GB of the game's buffers on the card", "2 Go de buffers du jeu sur la carte" },
		{ "2.  Graphics card: update the driver. RTX 3060/3060 Ti/3070/3080/3090 bought before April 2021 may need NVIDIA's", "2.  Carte graphique : mets le pilote à jour. Les RTX 3060/3060 Ti/3070/3080/3090 achetées avant avril 2021 peuvent avoir besoin de la" },
		{ "2: less memory, 4: faster", "2 : moins de mémoire, 4 : plus rapide" },
		{ "3.  Windows: games get it through D3D12's GPU upload heaps: Windows 11 24H2 or newer and a recent driver.", "3.  Windows : les jeux y accèdent via les GPU upload heaps de D3D12 : Windows 11 24H2 ou plus récent et un pilote récent." },
		{ "30 FPS clock", "horloge à 30 FPS" },
		{ "4-6 GB, 8-16 GB RAM", "4-6 Go, 8-16 Go de RAM" },
		{ "60 FPS clock", "horloge à 60 FPS" },
		{ "60 lets the game pass 30 FPS", "60 laisse le jeu dépasser 30 FPS" },
		{ "6-8 GB GPUs", "Cartes 6-8 Go" },
		{ "A code only holds the values on this page; the launcher shows what changes before it applies one.", "Un code ne contient que les valeurs de cette page ; le launcher montre ce qui change avant de l'appliquer." },
		{ "A VIctory session stopped answering: PLAY closes it and starts again.", "Une session VIctory ne répond plus : JOUER la ferme et relance." },
		{ "ABOUT", "À PROPOS" },
		{ "Activity", "Activité" },
		{ "Add it", "L'ajouter" },
		{ "Administrator rights were not given.", "Les droits administrateur n'ont pas été accordés." },
		{ "Advanced Tweaks (Custom preset)", "Réglages avancés (préréglage Personnalisé)" },
		{ "Advanced Tweaks", "Réglages avancés" },
		{ "AMD · VIctory-Belobug-AMD by Belobug", "AMD · VIctory-Belobug-AMD par Belobug" },
		{ "AMD card: tried the edupode engine", "Carte AMD : j'ai essayé le moteur edupode" },
		{ "AMD card: try the edupode engine?", "Carte AMD : essayer le moteur edupode ?" },
		{ "AMD cards play with VIctory-Belobug-AMD, Belobug's fork for Radeon (GPL-3.0). Downloads its source and builds it once.", "Les cartes AMD jouent avec VIctory-Belobug-AMD, le fork de Belobug pour Radeon (GPL-3.0). Télécharge son code et le compile une fois." },
		{ "AMD cards play with VIctory-Belobug-AMD, Belobug's fork tuned for Radeon (github.com/Belobug/VIctory-Belobug-AMD, GPL-3.0). All credit for it is Belobug's. Setup builds it once.", "Les cartes AMD jouent avec VIctory-Belobug-AMD, le fork de Belobug optimisé pour Radeon (github.com/Belobug/VIctory-Belobug-AMD, GPL-3.0). Tout le mérite revient à Belobug. Installation le compile une fois." },
		{ "AMD engine (Belobug)", "Moteur AMD (Belobug)" },
		{ "An update was refused: its list could not be read.", "Une mise à jour a été refusée : sa liste est illisible." },
		{ "An update was refused: its signature is not the project's.", "Une mise à jour a été refusée : sa signature n'est pas celle du projet." },
		{ "An Xbox controller connected to the PC replaces all of this.", "Une manette Xbox branchée au PC remplace tout ceci." },
		{ "Anti-stretch", "Anti-étirement" },
		{ "App", "Appli" },
		{ "Apply", "Appliquer" },
		{ "ASRock: Advanced > PCI Configuration > Above 4G Decoding: Enabled, then Re-Size BAR Support: Enabled.", "ASRock : Advanced > PCI Configuration > Above 4G Decoding : Enabled, puis Re-Size BAR Support : Enabled." },
		{ "ASUS: Advanced > PCI Subsystem Settings > Above 4G Decoding: Enabled, then Re-Size BAR Support: Auto/Enabled.", "ASUS : Advanced > PCI Subsystem Settings > Above 4G Decoding : Enabled, puis Re-Size BAR Support : Auto/Enabled." },
		{ "Auto-detect", "Détecter" },
		{ "Automatic (AMD cards: Belobug's AMD engine)", "Automatique (cartes AMD : moteur AMD de Belobug)" },
		{ "Back to Play", "Retour à Jouer" },
		{ "Balanced · edupode", "Équilibré · edupode" },
		{ "Belobug's AMD engine", "le moteur AMD de Belobug" },
		{ "best performance power (plugged in)", "alimentation en performances maximales (branché)" },
		{ "Binary missing", "Binaire manquant" },
		{ "Binary ready", "Binaire prêt" },
		{ "Black squares or wrong colours", "Carrés noirs ou mauvaises couleurs" },
		{ "Boot > CSM (Compatibility Support Module) > Launch CSM: Disabled.", "Boot > CSM (Compatibility Support Module) > Launch CSM : Disabled." },
		{ "Boot > CSM (Compatibility Support Module): Disabled.", "Boot > CSM (Compatibility Support Module) : Disabled." },
		{ "Boot > CSM Support: Disabled.", "Boot > CSM Support : Disabled." },
		{ "Browse...", "Parcourir..." },
		{ "Build it", "Le compiler" },
		{ "Build VIctory and choose the game folder first.", "Compile VIctory et choisis d'abord le dossier du jeu." },
		{ "Build VIctory first (Setup).", "Compile d'abord VIctory (Installation)." },
		{ "Build VIctory", "Compiler VIctory" },
		{ "Build", "Compiler" },
		{ "Building {0}... Log: ", "Compilation de {0}... Log : " },
		{ "Building {2}: {0} ({1} parts)", "Compilation de {2} : {0} ({1} parties)" },
		{ "Cache path is plain (no accents or symbols)", "Chemin du cache simple (sans accents ni symboles)" },
		{ "Cache: ", "Cache : " },
		{ "Cancel", "Annuler" },
		{ "Card: {0:0.0} of {1:0.0} GB in use  —  VIctory {2:0.0} GB, everything else {3:0.0} GB\r\nRAM: {4:0.0} of {5:0.0} GB free", "Carte : {0:0.0} sur {1:0.0} Go utilisés  —  VIctory {2:0.0} Go, le reste {3:0.0} Go\r\nRAM : {4:0.0} sur {5:0.0} Go libres" },
		{ "Check again", "Vérifier à nouveau" },
		{ "Check now", "Vérifier maintenant" },
		{ "Check the apps to close first.", "Coche d'abord les applis à fermer." },
		{ "Checking GitHub...", "Vérification sur GitHub..." },
		{ "Choose the folder of your dump.", "Choisis le dossier de ton dump." },
		{ "Choose the folder that holds the game's game_scarlett*.exe", "Choisis le dossier qui contient le game_scarlett*.exe du jeu" },
		{ "Choose the game folder first (Auto-detect).", "Choisis d'abord le dossier du jeu (Détecter)." },
		{ "Classic cgbj", "cgbj classique" },
		{ "CLOSE AND PLAY", "FERMER ET JOUER" },
		{ "Close game", "Fermer le jeu" },
		{ "Close stuck game", "Fermer le jeu bloqué" },
		{ "Close the checked apps", "Fermer les applis cochées" },
		{ "Close the game first.", "Ferme d'abord le jeu." },
		{ "Close the game now? What was not saved is lost.", "Fermer le jeu maintenant ? Ce qui n'est pas sauvegardé sera perdu." },
		{ "Close these apps? Unsaved work in them is lost.\r\n\r\n", "Fermer ces applis ? Le travail non enregistré sera perdu.\r\n\r\n" },
		{ "Closed VIctory (process {0}).", "VIctory fermé (processus {0})." },
		{ "Collecting...", "Collecte..." },
		{ "Compiles the emulator from this folder (several minutes, once; again after an update).", "Compile l'émulateur depuis ce dossier (plusieurs minutes, une fois ; à refaire après une mise à jour)." },
		{ "Configure", "Configurer" },
		{ "Controls", "Commandes" },
		{ "Copy ID", "Copier l'ID" },
		{ "Copy my profile", "Copier mon profil" },
		{ "Could not clear the cache: ", "Impossible de vider le cache : " },
		{ "Could not reach GitHub; trying again in 5 minutes.", "GitHub injoignable ; nouvel essai dans 5 minutes." },
		{ "Could not read the video memory counters (they need Windows 10 1709 or later).", "Impossible de lire les compteurs de VRAM (il faut Windows 10 1709 ou plus récent)." },
		{ "Could not start cargo: ", "Impossible de lancer cargo : " },
		{ "Could not start: ", "Démarrage impossible : " },
		{ "Custom", "Personnalisé" },
		{ "Decoupled simulation tick (no 30 FPS cap)", "Tick de simulation découplé (sans limite à 30 FPS)" },
		{ "Dedicated graphics card for VIctory", "Carte graphique dédiée pour VIctory" },
		{ "Defender's ransomware rule blocks freshly built programs ('Access denied'). Adds this folder's 'target' to its exceptions; Windows asks for permission.", "La règle anti-ransomware de Defender bloque les programmes fraîchement compilés ('Accès refusé'). Ajoute le dossier 'target' de ce dossier aux exclusions ; Windows demande l'autorisation." },
		{ "Delete the shader cache and prepare it again (about 5 minutes)?", "Supprimer le cache des shaders et le préparer à nouveau (environ 5 minutes) ?" },
		{ "Disk room: not read", "Espace disque : non lu" },
		{ "Do everything for me", "Tout faire pour moi" },
		{ "Done.", "Terminé." },
		{ "Done: {0}\r\n\r\nSend this zip on Discord, in the channel \"{1}\".\r\nA short summary is already copied: paste it with the zip.\r\n\r\nOpen Discord now?", "Terminé : {0}\r\n\r\nEnvoie ce zip sur Discord, dans le salon \"{1}\".\r\nUn court résumé est déjà copié : colle-le avec le zip.\r\n\r\nOuvrir Discord maintenant ?" },
		{ "Don't ask again", "Ne plus demander" },
		{ "Downloaded {0} into {1}", "{0} téléchargé dans {1}" },
		{ "Downloading {0}...", "Téléchargement de {0}..." },
		{ "Downloading v{0}...", "Téléchargement de la v{0}..." },
		{ "Driver changed ({0} → {1}): PLAY remakes the pipelines once", "Pilote changé ({0} → {1}) : JOUER refait les pipelines une fois" },
		{ "Driver frame generation for RTX 40/50, written into the driver's profile for victory.exe (no NVIDIA App needed).", "Génération d'images du pilote pour RTX 40/50, écrite dans le profil du pilote pour victory.exe (sans NVIDIA App)." },
		{ "Driver frame generation: the card shows a made-up frame between two of the game's. Now: ", "Génération d'images du pilote : la carte affiche une image inventée entre deux images du jeu. Actuellement : " },
		{ "Dump verified", "Dump vérifié" },
		{ "Each step checks your PC. Press its button, or 'Do everything for me' to run the missing ones in order. Windows asks before anything that needs administrator rights.", "Chaque étape vérifie ton PC. Appuie sur son bouton, ou sur 'Tout faire pour moi' pour lancer celles qui manquent dans l'ordre. Windows demande avant tout ce qui nécessite les droits administrateur." },
		{ "edupode (NVIDIA)", "edupode (NVIDIA)" },
		{ "Emulator", "Émulateur" },
		{ "Engine        ", "Moteur        " },
		{ "Engine", "Moteur" },
		{ "Engine: ", "Moteur : " },
		{ "Exactly what is sent:", "Exactement ce qui est envoyé :" },
		{ "Experimental: textures the game builds on the CPU no longer wait for the game's memory to settle. Shorter loading pauses; untested.", "Expérimental : les textures que le jeu prépare sur le processeur n'attendent plus que la mémoire du jeu se stabilise. Pauses de chargement plus courtes ; non testé." },
		{ "Fast loading (experimental)", "Chargement rapide (expérimental)" },
		{ "Fastest", "Le plus rapide" },
		{ "Fewer stretched bodies; several FPS slower", "Moins de corps étirés ; plusieurs FPS en moins" },
		{ "Find it", "Le trouver" },
		{ "Fix it", "Corriger" },
		{ "For 6-8 GB cards and 16 GB of RAM: 540p, textures over 1024 texels halved, fewer of the game's buffers on the card, smaller texture batches, the memory guards on. Less video memory and RAM, a softer picture.", "Pour les cartes de 6-8 Go et 16 Go de RAM : 540p, textures de plus de 1024 texels divisées par deux, moins de buffers du jeu sur la carte, lots de textures plus petits, protections mémoire activées. Moins de VRAM et de RAM, image plus douce." },
		{ "For the weakest PCs (4-6 GB cards, 8-16 GB of RAM): 540p, textures capped at 512 texels, the fewest buffers on the card, small texture batches, the memory guards on and a 30 FPS clock. The least memory of all; a soft picture. Give Windows a large page file (Setup).", "Pour les PC les plus faibles (cartes de 4-6 Go, 8-16 Go de RAM) : 540p, textures limitées à 512 texels, le minimum de buffers sur la carte, petits lots de textures, protections mémoire activées et horloge à 30 FPS. Le moins de mémoire possible ; image douce. Donne à Windows un gros fichier d'échange (Installation)." },
		{ "FPS clock", "Horloge FPS" },
		{ "FPS, frame time, VRAM & RAM", "FPS, temps d'image, VRAM et RAM" },
		{ "Frame gen n/a", "Génération d'images n/d" },
		{ "Frame generation is on (experimental). Tell us how it runs on Discord!", "Génération d'images activée (expérimental). Dis-nous sur Discord comment ça tourne !" },
		{ "Frame generation test (how it ran)", "Test de la génération d'images (comment ça a tourné)" },
		{ "Frame generation", "Génération d'images" },
		{ "Free on the cache's disk: {0:0.0} GB  (12 GB needed to prepare)", "Libre sur le disque du cache : {0:0.0} Go  (12 Go nécessaires pour préparer)" },
		{ "Game buffers on the card", "Buffers du jeu sur la carte" },
		{ "Game closed. Preparing this session's pipelines for the next one...", "Jeu fermé. Préparation des pipelines de cette session pour la prochaine..." },
		{ "Game errors", "Erreurs du jeu" },
		{ "Game executable: ", "Exécutable du jeu : " },
		{ "Game found: ", "Jeu trouvé : " },
		{ "Game", "Jeu" },
		{ "Getting the most video memory", "Obtenir le maximum de VRAM" },
		{ "Gigabyte/AORUS: Settings > IO Ports > Above 4G Decoding: Enabled, then Re-Size BAR Support: Auto.", "Gigabyte/AORUS : Settings > IO Ports > Above 4G Decoding : Enabled, puis Re-Size BAR Support : Auto." },
		{ "Graphics card ", "Carte graphique " },
		{ "Graphics card for VIctory: {0}; Resizable BAR for the game: {1}.", "Carte graphique pour VIctory : {0} ; Resizable BAR pour le jeu : {1}." },
		{ "Graphics driver {0}", "Pilote graphique {0}" },
		{ "Graphics driver: not read", "Pilote graphique : non lu" },
		{ "Hardware & ID", "Matériel et ID" },
		{ "Hardware sharing (only hardware)", "Partage du matériel (uniquement le matériel)" },
		{ "Hardware sharing is on. Thank you!", "Partage du matériel activé. Merci !" },
		{ "Hardware", "Matériel" },
		{ "Help the game run on more PCs", "Aide le jeu à tourner sur plus de PC" },
		{ "Help", "Aide" },
		{ "Help: everything the launcher does", "Aide : tout ce que fait le launcher" },
		{ "Hold texture prefetch back early (16 GB of RAM or less)", "Freinent tôt le préchargement des textures (16 Go de RAM ou moins)" },
		{ "I can't turn it on", "Je ne peux pas l'activer" },
		{ "ID copied: ", "ID copié : " },
		{ "If you cannot turn it on (older card, laptop without the option, Windows 10):", "Si tu ne peux pas l'activer (carte ancienne, portable sans l'option, Windows 10) :" },
		{ "In a vehicle (after F, high beep)", "En véhicule (après F, bip aigu)" },
		{ "In your words (what you did, what you saw):", "Avec tes mots (ce que tu as fait, ce que tu as vu) :" },
		{ "Install now", "Installer maintenant" },
		{ "Install updates on their own", "Installer les mises à jour automatiquement" },
		{ "Install", "Installer" },
		{ "Installing the update failed: ", "L'installation de la mise à jour a échoué : " },
		{ "Join the Discord", "Rejoindre le Discord" },
		{ "Julia's Launcher is edupode's remake, built on the work of:", "Julia's Launcher est le remake d'edupode, construit sur le travail de :" },
		{ "Julia's Launcher was updated to v{0}. It opens again now.", "Julia's Launcher a été mis à jour en v{0}. Il se rouvre maintenant." },
		{ "Keep Belobug's", "Garder celui de Belobug" },
		{ "Keep the most video memory", "Garder le max de VRAM" },
		{ "Keeps 2 GB of the game's buffers on the card instead of 1.5: for crowds and fights, where the FPS drops. Needs a 12 GB card. Tested on edupode's PC: crowds 14-17 FPS instead of 10-15.", "Garde 2 Go de buffers du jeu sur la carte au lieu de 1,5 : pour les foules et les combats, où les FPS chutent. Nécessite une carte de 12 Go. Testé sur le PC d'edupode : foules à 14-17 FPS au lieu de 10-15." },
		{ "Key", "Touche" },
		{ "Keyboard and mouse", "Clavier et souris" },
		{ "Laptops (Acer, Lenovo, HP, Dell...): usually no switch; it comes with the maker's BIOS update for your model.", "Portables (Acer, Lenovo, HP, Dell...) : en général pas d'option ; elle arrive avec la mise à jour BIOS du fabricant pour ton modèle." },
		{ "Largest texture", "Plus grande texture" },
		{ "Last check: ", "Dernière vérification : " },
		{ "Last errors (prepare.log)", "Dernières erreurs (prepare.log)" },
		{ "Last preparation finished well", "La dernière préparation s'est bien terminée" },
		{ "Later", "Plus tard" },
		{ "Launch options", "Options de lancement" },
		{ "Launcher errors", "Erreurs du launcher" },
		{ "Less video memory, more pop-in", "Moins de VRAM, plus de textures qui apparaissent tard" },
		{ "Lets the CPU write straight into all of the video memory (faster copies). It is switched on in the PC's BIOS/UEFI: the guide shows how.", "Permet au processeur d'écrire directement dans toute la VRAM (copies plus rapides). Ça s'active dans le BIOS/UEFI du PC : le guide montre comment." },
		{ "Load pipelines before the game", "Charger les pipelines avant le jeu" },
		{ "Loading pipelines...", "Chargement des pipelines..." },
		{ "Loading pipelines: {0} of {1}", "Chargement des pipelines : {0} sur {1}" },
		{ "Loading pipelines: 0 of {0}", "Chargement des pipelines : 0 sur {0}" },
		{ "Look for 'Above 4G Decoding' and 'Re-Size BAR' (or 'Resizable BAR', 'Smart Access Memory') under Advanced > PCI, and turn CSM off under Boot.", "Cherche 'Above 4G Decoding' et 'Re-Size BAR' (ou 'Resizable BAR', 'Smart Access Memory') dans Advanced > PCI, et désactive le CSM dans Boot." },
		{ "Looking for game_scarlett*.exe...", "Recherche de game_scarlett*.exe..." },
		{ "Looking...", "Recherche..." },
		{ "Looks for game_scarlett_master.exe (or another game_scarlett*.exe) in Downloads, Desktop, Documents, Games folders and the root of each drive.", "Cherche game_scarlett_master.exe (ou un autre game_scarlett*.exe) dans Téléchargements, Bureau, Documents, les dossiers Games et la racine de chaque disque." },
		{ "Low FPS or stutter", "FPS bas ou saccades" },
		{ "Made from parts of your PC (the motherboard and the processor): it stays the same after reinstalling Windows. Put it in your bug reports.", "Calculé à partir des composants de ton PC (carte mère et processeur) : il reste le même après une réinstallation de Windows. Mets-le dans tes rapports de bug." },
		{ "Make the package", "Créer le paquet" },
		{ "Memory guards", "Protections mémoire" },
		{ "Modest PC", "PC modeste" },
		{ "more: crowds and fights hold up", "plus : foules et combats tiennent mieux" },
		{ "Motherboard   ", "Carte mère    " },
		{ "MSI: Settings > Advanced > PCI Subsystem Settings > Above 4G memory/Crypto Currency mining: Enabled, then Re-Size BAR Support: Enabled", "MSI : Settings > Advanced > PCI Subsystem Settings > Above 4G memory/Crypto Currency mining : Enabled, puis Re-Size BAR Support : Enabled" },
		{ "My shader is not rendering properly", "Les shaders ne s'affichent pas bien" },
		{ "native 1080p", "1080p natif" },
		{ "Native desktop presentation", "Présentation native sur le bureau" },
		{ "New: asks Windows to reserve video memory for the game, so other apps are moved out first when memory runs short. Fewer black and flat textures.", "Nouveau : demande à Windows de réserver de la VRAM pour le jeu, donc les autres applis sortent en premier quand la mémoire manque. Moins de textures noires et plates." },
		{ "New: before the game starts, a loading bar makes every pipeline earlier sessions drew with, in separate processes, so the driver has them ready: no stutter the first time you see a place. Takes seconds once the cache is ready; after a VIctory or driver update it takes longer, once.", "Nouveau : avant le lancement du jeu, une barre de chargement prépare toutes les pipelines utilisées lors des sessions précédentes, dans des processus séparés, pour que le pilote les ait déjà : pas de saccade la première fois que tu vois un lieu. Quelques secondes une fois le cache prêt ; après une mise à jour de VIctory ou du pilote, plus long, une fois." },
		{ "New: frame generation for your card (experimental)", "Nouveau : génération d'images pour ta carte (expérimental)" },
		{ "New: NVIDIA's driver frame generation for RTX 40/50. The launcher writes it into the driver's profile for victory.exe, no NVIDIA App needed. Doubles the frames shown; adds a little input lag.", "Nouveau : la génération d'images du pilote NVIDIA pour RTX 40/50. Le launcher l'écrit dans le profil du pilote pour victory.exe, sans NVIDIA App. Double les images affichées ; ajoute un peu de latence." },
		{ "no cap", "sans limite" },
		{ "No game dump", "Pas de dump du jeu" },
		{ "No stutter the first time you see a place", "Pas de saccade la première fois que tu vois un lieu" },
		{ "No update: this is the latest version.", "Pas de mise à jour : c'est la dernière version." },
		{ "no", "non" },
		{ "Not an NVIDIA card: only the game's side is checked.", "Pas une carte NVIDIA : seul le côté du jeu est vérifié." },
		{ "Not built yet: press 'Build VIctory' (needs Rust and Visual Studio Build Tools).", "Pas encore compilé : appuie sur 'Compiler VIctory' (nécessite Rust et Visual Studio Build Tools)." },
		{ "Not in the AMD engine yet (pick the edupode engine on Hardware & ID to try it)", "Pas encore dans le moteur AMD (choisis le moteur edupode dans Matériel et ID pour l'essayer)" },
		{ "Not now", "Pas maintenant" },
		{ "Not prepared yet, or not since the last update (press Prepare shaders)", "Pas encore préparé, ou pas depuis la dernière mise à jour (appuie sur Préparer les shaders)" },
		{ "not set yet (PLAY sets it when the option is on)", "pas encore réglé (JOUER le règle quand l'option est activée)" },
		{ "Note", "Remarque" },
		{ "NVIDIA firmware update", "Firmware NVIDIA" },
		{ "NVIDIA Smooth Motion (RTX 40/50, set up for you)", "NVIDIA Smooth Motion (RTX 40/50, configuré pour toi)" },
		{ "NVIDIA's driver uses Resizable BAR only for the games on its list: this puts victory.exe on it (rBAR Enable, Enable v2, Options in its profile).", "Le pilote NVIDIA n'utilise le Resizable BAR que pour les jeux de sa liste : ceci y ajoute victory.exe (rBAR Enable, Enable v2, Options dans son profil)." },
		{ "NVSmooth30 (experimental) is on for this session.", "NVSmooth30 (expérimental) est activé pour cette session." },
		{ "NVSmooth30 for RTX 30 (experimental)", "NVSmooth30 pour RTX 30 (expérimental)" },
		{ "Off by default. Only the list below; never logs, options, paths, names or your IP.", "Désactivé par défaut. Uniquement la liste ci-dessous ; jamais de logs, d'options, de chemins, de noms ni ton IP." },
		{ "off for victory.exe", "désactivé pour victory.exe" },
		{ "off", "désactivé" },
		{ "Off", "Désactivé" },
		{ "Off: nothing is sent.", "Désactivé : rien n'est envoyé." },
		{ "On a laptop with two graphics cards, Windows may give VIctory the slow built-in one. Sets 'High performance' for victory.exe (Windows > Graphics settings).", "Sur un portable avec deux cartes graphiques, Windows peut donner à VIctory l'intégrée, lente. Règle 'Hautes performances' pour victory.exe (Windows > Paramètres graphiques)." },
		{ "On foot", "À pied" },
		{ "ON for victory.exe", "ACTIVÉ pour victory.exe" },
		{ "On in the BIOS and on the card, but Windows does not offer it to games (see 3).", "Activé dans le BIOS et sur la carte, mais Windows ne le propose pas aux jeux (voir 3)." },
		{ "on in the BIOS, not offered by Windows (see Setup)", "activé dans le BIOS, pas proposé par Windows (voir Installation)" },
		{ "On", "activé" },
		{ "on", "activé" },
		{ "On: sent at most once a day, to a database only edupode can read. Turn it off any time.", "Activé : envoyé au plus une fois par jour, vers une base de données que seul edupode peut lire. Désactivable à tout moment." },
		{ "Only {0:0.0} GB free on {1}: the shaders need about 12 GB. Free some space and try again.", "Seulement {0:0.0} Go libres sur {1} : les shaders ont besoin d'environ 12 Go. Libère de l'espace et réessaie." },
		{ "Open Discord", "Ouvrir Discord" },
		{ "Open the cache folder", "Ouvrir le dossier du cache" },
		{ "Open the logs folder", "Ouvrir le dossier des logs" },
		{ "or turn off its hardware acceleration", "ou désactive son accélération matérielle" },
		{ "Other apps already hold {0:0.0} GB of the card: the Performance page frees it.", "D'autres applis occupent déjà {0:0.0} Go de la carte : la page Performances la libère." },
		{ "Pad", "Manette" },
		{ "part of other apps (protected)", "fait partie d'autres applis (protégé)" },
		{ "Paste a profile code here:", "Colle un code de profil ici :" },
		{ "Peak FPS", "FPS max" },
		{ "Performance", "Performances" },
		{ "Pipelines made for the driver: {0:N0}  (used by your sessions: {1:N0})", "Pipelines faites pour le pilote : {0:N0}  (utilisées par tes sessions : {1:N0})" },
		{ "Pipelines ready.", "Pipelines prêtes." },
		{ "Pipelines that failed: {0:N0}", "Pipelines en échec : {0:N0}" },
		{ "Pipelines were already ready.", "Les pipelines étaient déjà prêtes." },
		{ "Pipelines: {0:N0} made for the driver (your sessions used {1:N0})", "Pipelines : {0:N0} faites pour le pilote (tes sessions en ont utilisé {1:N0})" },
		{ "Pipelines: {0:N0} of {1:N0} loaded before the game · {2} s", "Pipelines : {0:N0} sur {1:N0} chargées avant le jeu · {2} s" },
		{ "Pipelines: {0:N0} of {1:N0} made · {2} s", "Pipelines : {0:N0} sur {1:N0} faites · {2} s" },
		{ "Pipelines: after the shaders", "Pipelines : après les shaders" },
		{ "Pipelines: all ready", "Pipelines : toutes prêtes" },
		{ "Pipelines: done", "Pipelines : terminé" },
		{ "PLAY does nothing / stays grey", "JOUER ne fait rien / reste gris" },
		{ "Play with these (Custom)", "Jouer avec ceux-ci (Personnalisé)" },
		{ "Play", "Jouer" },
		{ "Point at an option to see what it does. ★ marks what was tested on edupode's PC (RTX 5070 Ti laptop).", "Survole une option pour voir ce qu'elle fait. ★ marque ce qui a été testé sur le PC d'edupode (portable RTX 5070 Ti)." },
		{ "Power: plugged in, best performance", "Alimentation : branché, performances maximales" },
		{ "Prepare shaders", "Préparer les shaders" },
		{ "Prepare", "Préparer" },
		{ "Prepared (by another way of starting VIctory, or before this launcher)", "Préparé (par un autre moyen de lancer VIctory, ou avant ce launcher)" },
		{ "prepared shaders", "shaders préparés" },
		{ "Preparing pipelines stopped (code {0}); the shaders are ready, PLAY makes the pipelines.", "La préparation des pipelines s'est arrêtée (code {0}) ; les shaders sont prêts, JOUER fait les pipelines." },
		{ "Preparing pipelines...", "Préparation des pipelines..." },
		{ "Preparing pipelines: {0} of {1}", "Préparation des pipelines : {0} sur {1}" },
		{ "Preparing shaders fails", "La préparation des shaders échoue" },
		{ "Processor     ", "Processeur    " },
		{ "Profile applied: ", "Profil appliqué : " },
		{ "Profile code copied: ", "Code de profil copié : " },
		{ "Rainbow-face fix", "Correctif visages arc-en-ciel" },
		{ "ready", "prêt" },
		{ "ReBAR Off", "ReBAR désactivé" },
		{ "ReBAR Off?", "ReBAR désactivé ?" },
		{ "ReBAR On (not used by Windows)", "ReBAR activé (non utilisé par Windows)" },
		{ "ReBAR On", "ReBAR activé" },
		{ "Rebuild everything", "Tout refaire" },
		{ "rebuilt to 1080p by the game's upscaler", "reconstruite en 1080p par l'upscaler du jeu" },
		{ "Recommended", "Recommandé" },
		{ "Reference", "Référence" },
		{ "Refresh", "Actualiser" },
		{ "Render preset", "Préréglage de rendu" },
		{ "Repair", "Réparer" },
		{ "Repair: makes the missing shaders and retries the pipelines that failed. Rebuild everything: clears the cache and prepares it again (about 5 minutes).", "Réparer : fait les shaders manquants et réessaie les pipelines en échec. Tout refaire : vide le cache et le prépare à nouveau (environ 5 minutes)." },
		{ "Resizable BAR in the NVIDIA driver", "Resizable BAR dans le pilote NVIDIA" },
		{ "Resizable BAR is on in victory.exe's driver profile.", "Resizable BAR activé dans le profil du pilote de victory.exe." },
		{ "Resizable BAR shows off but it is on", "Le Resizable BAR apparaît désactivé alors qu'il est activé" },
		{ "Resizable BAR", "Resizable BAR" },
		{ "Resizable BAR: no more warnings. The game runs without it.", "Resizable BAR : plus d'avertissements. Le jeu tourne sans." },
		{ "Restart into the BIOS", "Redémarrer dans le BIOS" },
		{ "Running without: ", "Lancé sans : " },
		{ "RUNNING", "EN COURS" },
		{ "Rust (builds VIctory)", "Rust (compile VIctory)" },
		{ "Save your work first. The PC restarts in 5 seconds and opens its BIOS/UEFI settings. Restart now?", "Enregistre d'abord ton travail. Le PC redémarre dans 5 secondes et ouvre ses réglages BIOS/UEFI. Redémarrer maintenant ?" },
		{ "Send errors to Discord", "Envoyer les erreurs sur Discord" },
		{ "Send it on Discord", "Envoie-le sur Discord" },
		{ "Set it", "Régler" },
		{ "Sets Windows' power mode to 'Best performance'. A laptop on battery runs the CPU slower: plug it in (and use the maker's performance mode, e.g. PredatorSense).", "Met le mode d'alimentation de Windows sur 'Performances maximales'. Un portable sur batterie ralentit le processeur : branche-le (et utilise le mode performances du fabricant, ex. PredatorSense)." },
		{ "Setup", "Installation" },
		{ "Setup: everything done for you", "Installation : tout est fait pour toi" },
		{ "Shader and pipeline health (live)", "État des shaders et pipelines (en direct)" },
		{ "Shader cache cleared.", "Cache des shaders vidé." },
		{ "Shaders not prepared", "Shaders non préparés" },
		{ "Shaders prepared", "Shaders préparés" },
		{ "Shaders ready. You can press PLAY.", "Shaders prêts. Tu peux appuyer sur JOUER." },
		{ "Shaders translated: {0:N0}  ({1:0.0} GB)", "Shaders traduits : {0:N0}  ({1:0.0} Go)" },
		{ "Shaders", "Shaders" },
		{ "Shaders: {0:N0} translated · {1:0}% of the game's files · {2} s", "Shaders : {0:N0} traduits · {1:0} % des fichiers du jeu · {2} s" },
		{ "Shaders: {0:N0} translated, ready", "Shaders : {0:N0} traduits, prêts" },
		{ "Shaders: done", "Shaders : terminé" },
		{ "Shaders: failed (see the errors below)", "Shaders : échec (voir les erreurs ci-dessous)" },
		{ "Shaders: not prepared yet (press Prepare shaders)", "Shaders : pas encore préparés (appuie sur Préparer les shaders)" },
		{ "Shaders: starting...", "Shaders : démarrage..." },
		{ "Share a profile", "Partager un profil" },
		{ "Share my hardware with edupode", "Partager mon matériel avec edupode" },
		{ "Share your hardware?", "Partager ton matériel ?" },
		{ "Sharp", "Net" },
		{ "Shorter loading pauses; untested", "Pauses de chargement plus courtes ; non testé" },
		{ "Show me how", "Montre-moi comment" },
		{ "smaller: less RAM", "plus petit : moins de RAM" },
		{ "smaller: less video memory", "plus petit : moins de VRAM" },
		{ "Smooth Motion is off for victory.exe.", "Smooth Motion est désactivé pour victory.exe." },
		{ "Smooth Motion is on for victory.exe (driver profile).", "Smooth Motion est activé pour victory.exe (profil du pilote)." },
		{ "Start from a preset...", "Partir d'un préréglage..." },
		{ "Starting ", "Lancement de " },
		{ "Starting the game...", "Lancement du jeu..." },
		{ "System disk   ", "Disque système " },
		{ "system or needed (protected)", "système ou nécessaire (protégé)" },
		{ "Texture batch size", "Taille des lots de textures" },
		{ "Texture blocks at once", "Blocs de textures à la fois" },
		{ "Texture levels on demand", "Niveaux de textures à la demande" },
		{ "Textures finish uploading first; costs FPS", "Les textures finissent de se charger d'abord ; coûte des FPS" },
		{ "the AMD engine (Belobug)", "le moteur AMD (Belobug)" },
		{ "The AMD engine is not built yet: Setup > 'AMD engine (Belobug)'.", "Le moteur AMD n'est pas encore compilé : Installation > 'Moteur AMD (Belobug)'." },
		{ "The build failed. The reason is in the Activity box and in ", "La compilation a échoué. La raison est dans la case Activité et dans " },
		{ "The build failed: see ", "La compilation a échoué : voir " },
		{ "The C++ linker and Windows SDK Rust needs. A large download (2-6 GB); Windows asks for permission.", "L'éditeur de liens C++ et le SDK Windows dont Rust a besoin. Gros téléchargement (2-6 Go) ; Windows demande l'autorisation." },
		{ "The cache path has accents or symbols: move it (launcher.ini cache=)", "Le chemin du cache contient des accents ou des symboles : déplace-le (launcher.ini cache=)" },
		{ "The card reports a 256 MB BAR: off in the BIOS, or the card's firmware lacks it.", "La carte indique un BAR de 256 Mo : désactivé dans le BIOS, ou absent du firmware de la carte." },
		{ "The card reports a large BAR (nvidia-smi: BAR1 of 1 GB or more).", "La carte indique un grand BAR (nvidia-smi : BAR1 de 1 Go ou plus)." },
		{ "The compiler VIctory is written for. Installed with winget (rustup).", "Le compilateur pour lequel VIctory est écrit. Installé avec winget (rustup)." },
		{ "The database is not online yet: nothing is sent, even with sharing on.", "La base de données n'est pas encore en ligne : rien n'est envoyé, même avec le partage activé." },
		{ "the dedicated graphics card for VIctory", "la carte graphique dédiée pour VIctory" },
		{ "The dump has empty files ({0}): download or extract it again.", "Le dump contient des fichiers vides ({0}) : télécharge-le ou extrais-le à nouveau." },
		{ "The game and VIctory commit 25+ GB; a fixed, small page file ends them. Windows asks for permission; it counts after a restart.", "Le jeu et VIctory réservent plus de 25 Go ; un petit fichier d'échange fixe les fait planter. Windows demande l'autorisation ; effectif après redémarrage." },
		{ "The game closes or crashes", "Le jeu se ferme ou plante" },
		{ "The game closes or freezes, black squares, shaders, low FPS", "Le jeu se ferme ou se fige, carrés noirs, shaders, FPS bas" },
		{ "The game does not get it.", "Le jeu ne l'obtient pas." },
		{ "The game ended with an error (code {0}). Log: {1}", "Le jeu s'est terminé avec une erreur (code {0}). Log : {1}" },
		{ "The game folder has {0} executables; using {1} (change it beside the folder).", "Le dossier du jeu contient {0} exécutables ; utilisation de {1} (change-le à côté du dossier)." },
		{ "The game gets it: D3D12 offers mapped video memory.", "Le jeu l'obtient : D3D12 propose la VRAM mappée." },
		{ "The game is already running ('Close game' ends it).", "Le jeu tourne déjà ('Fermer le jeu' l'arrête)." },
		{ "The game was not found: choose its folder with Browse on the Play page.", "Le jeu est introuvable : choisis son dossier avec Parcourir dans la page Jouer." },
		{ "The game will run worse (or close) without these:\r\n\r\n", "Le jeu tournera moins bien (ou se fermera) sans ceci :\r\n\r\n" },
		{ "the game", "le jeu" },
		{ "The game's files are damaged or incomplete (malformed image). Download or extract the dump again.", "Les fichiers du jeu sont abîmés ou incomplets (malformed image). Télécharge ou extrais le dump à nouveau." },
		{ "The graphics card could not start D3D12, so the window stays white. Update the graphics driver and restart the PC.", "La carte graphique n'a pas pu démarrer D3D12, donc la fenêtre reste blanche. Mets à jour le pilote graphique et redémarre le PC." },
		{ "The last session had stopped answering: closing it.", "La dernière session ne répondait plus : fermeture." },
		{ "The launcher closes, a button does nothing, Setup or an update fails", "Le launcher se ferme, un bouton ne fait rien, Installation ou une mise à jour échoue" },
		{ "The launcher is not inside the source folder: downloading the source (main branch)...", "Le launcher n'est pas dans le dossier du code source : téléchargement du code (branche main)..." },
		{ "The original cgbj settings at 1080p, no texture changes. For comparison.", "Les réglages cgbj d'origine en 1080p, sans changement de textures. Pour comparer." },
		{ "The package could not be made: ", "Impossible de créer le paquet : " },
		{ "The PC starts in Legacy/CSM mode: Resizable BAR needs UEFI (convert the disk with mbr2gpt first).", "Le PC démarre en mode Legacy/CSM : le Resizable BAR nécessite l'UEFI (convertis d'abord le disque avec mbr2gpt)." },
		{ "The PC starts in UEFI mode (needed).", "Le PC démarre en mode UEFI (nécessaire)." },
		{ "the source", "le code source" },
		{ "The world at full 1080p with held texture levels. Needs a strong GPU with 12 GB or more; slower in the open world.", "Le monde en 1080p complet avec niveaux de textures conservés. Nécessite une carte puissante de 12 Go ou plus ; plus lent en monde ouvert." },
		{ "The world drawn at 1280x720 and rebuilt to 1080p by the game's own upscaler, texture levels held (video memory under budget), 60 FPS clock. About 22-26 FPS in the open world and 35+ in cutscenes on an RTX 5070 Ti laptop (6-8 GB of video memory, ~20 GB of RAM).", "Le monde rendu en 1280x720 et reconstruit en 1080p par l'upscaler du jeu, niveaux de textures conservés (VRAM sous la limite), horloge à 60 FPS. Environ 22-26 FPS en monde ouvert et 35+ en cinématiques sur un portable RTX 5070 Ti (6-8 Go de VRAM, ~20 Go de RAM)." },
		{ "The world drawn at 960x540: the most frames, a softer picture. For slower CPUs.", "Le monde rendu en 960x540 : le plus d'images, une image plus douce. Pour les processeurs plus lents." },
		{ "This code cannot be used: ", "Ce code est inutilisable : " },
		{ "This code changes:\r\n\r\n", "Ce code change :\r\n\r\n" },
		{ "This profile is the same as yours.", "Ce profil est identique au tien." },
		{ "Translates the game's shaders and makes their pipelines once (about 5 minutes), so the game does not stop to do it.", "Traduit les shaders du jeu et prépare leurs pipelines une fois (environ 5 minutes), pour que le jeu ne s'arrête pas pour le faire." },
		{ "Translating the game's shaders (about 5 minutes)...", "Traduction des shaders du jeu (environ 5 minutes)..." },
		{ "Translating the game's shaders and preparing pipelines (about 5 minutes)... Log: ", "Traduction des shaders du jeu et préparation des pipelines (environ 5 minutes)... Log : " },
		{ "Translating the shaders failed (code {0}).", "La traduction des shaders a échoué (code {0})." },
		{ "Try the edupode engine", "Essayer le moteur edupode" },
		{ "Turn it off", "Désactiver" },
		{ "Turn it on now", "Activer maintenant" },
		{ "turn off Instant Replay / overlay in NVIDIA App", "désactive Instant Replay / l'overlay dans NVIDIA App" },
		{ "Turn on", "Activer" },
		{ "Ultra Low", "Ultra bas" },
		{ "Up to date (no release published yet).", "À jour (aucune version publiée pour l'instant)." },
		{ "Up to date (v{0}).", "À jour (v{0})." },
		{ "Update check failed: ", "La vérification a échoué : " },
		{ "Update v{0} downloaded and checked.", "Mise à jour v{0} téléchargée et vérifiée." },
		{ "Update v{0} ready", "Mise à jour v{0} prête" },
		{ "Updated to v{0}.", "Mis à jour en v{0}." },
		{ "Updates come only from github.com/felicienradio/cold-project-vi releases. Each one is signed with the project's release key; a file is installed only when the signature and its SHA-256 match. Anyone can check a release's files against the source code (README: 'Checking a release').", "Les mises à jour viennent uniquement des versions de github.com/felicienradio/cold-project-vi. Chacune est signée avec la clé de publication du projet ; un fichier n'est installé que si la signature et son SHA-256 correspondent. Tout le monde peut comparer les fichiers d'une version au code source (README : 'Checking a release')." },
		{ "Updates", "Mises à jour" },
		{ "v{0} is ready: it installs when the game closes.", "La v{0} est prête : elle s'installe à la fermeture du jeu." },
		{ "v{0} is ready: press 'Install now'.", "La v{0} est prête : appuie sur 'Installer maintenant'." },
		{ "v{0} was refused: {1} does not match its SHA-256.", "La v{0} a été refusée : {1} ne correspond pas à son SHA-256." },
		{ "VI frame gen ", "Génération VI " },
		{ "VI frame generation: any card, a frame between each two (experimental)", "Génération d'images VI : toute carte, une image entre chaque paire (expérimental)" },
		{ "VIctory is already running. Close it and start again?", "VIctory tourne déjà. Le fermer et relancer ?" },
		{ "VIctory runs on ", "VIctory tourne sur " },
		{ "Video memory and RAM", "VRAM et RAM" },
		{ "Video memory", "VRAM" },
		{ "Virtual memory could not be set. By hand: Windows key > 'Advanced system settings' > Performance: Settings > Advanced > Virtual memory: Change > tick 'Automatically manage paging file size for all drives' > OK, then restart.", "La mémoire virtuelle n'a pas pu être réglée. À la main : touche Windows > 'Paramètres système avancés' > Performances : Paramètres > Avancé > Mémoire virtuelle : Modifier > coche 'Gestion automatique du fichier d'échange pour les lecteurs' > OK, puis redémarre." },
		{ "Virtual memory is now managed by Windows. Restart Windows for it to count.", "La mémoire virtuelle est maintenant gérée par Windows. Redémarre Windows pour que ce soit pris en compte." },
		{ "virtual memory managed by Windows", "mémoire virtuelle gérée par Windows" },
		{ "Virtual memory not turning on", "La mémoire virtuelle ne s'active pas" },
		{ "Visual Studio Build Tools (C++)", "Visual Studio Build Tools (C++)" },
		{ "Wait for the current task to finish.", "Attends la fin de la tâche en cours." },
		{ "WAIT...", "PATIENTE..." },
		{ "Waits for every copy of the game's buffers before drawing: fewer stretched bodies ('spaghetti'), but several FPS slower.", "Attend chaque copie des buffers du jeu avant de dessiner : moins de corps étirés ('spaghetti'), mais plusieurs FPS en moins." },
		{ "Waits for textures to finish uploading before use: fixes rainbow faces, costs FPS.", "Attend que les textures finissent de se charger avant de les utiliser : corrige les visages arc-en-ciel, coûte des FPS." },
		{ "What changed", "Ce qui a changé" },
		{ "What happened? (tick any)", "Que s'est-il passé ? (coche ce qui s'applique)" },
		{ "What kind of problem?", "Quel type de problème ?" },
		{ "While the game is not running; checked every 5 minutes", "Quand le jeu ne tourne pas ; vérifié toutes les 5 minutes" },
		{ "Windows Defender exception", "Exclusion Windows Defender" },
		{ "Windows desktop: see the MUX tip below", "Bureau Windows : voir l'astuce MUX ci-dessous" },
		{ "Windows now gives victory.exe the high-performance graphics card (restart the game to apply).", "Windows donne maintenant à victory.exe la carte graphique hautes performances (relance le jeu pour appliquer)." },
		{ "Windows ran out of memory to start the graphics (0x8007000E), so the window stays white.\r\n\r\nClose other apps, let Windows manage virtual memory (Setup) and restart the PC, then play again.", "Windows a manqué de mémoire pour démarrer les graphismes (0x8007000E), donc la fenêtre reste blanche.\r\n\r\nFerme d'autres applis, laisse Windows gérer la mémoire virtuelle (Installation) et redémarre le PC, puis rejoue." },
		{ "Windows ready", "Windows prêt" },
		{ "Windows reserves video memory for the game", "Windows réserve de la VRAM pour le jeu" },
		{ "Windows settings, Resizable BAR and shaders are all set.", "Réglages Windows, Resizable BAR et shaders : tout est prêt." },
		{ "winget is missing (App Installer from the Microsoft Store). Opening the download page.", "winget est absent (Programme d'installation d'application du Microsoft Store). Ouverture de la page de téléchargement." },
		{ "World resolution", "Résolution du monde" },
		{ "Xbox Series-to-Windows compatibility runtime · ", "Runtime de compatibilité Xbox Series vers Windows · " },
		{ "yes", "oui" },
		{ "Yes, share my hardware", "Oui, partager mon matériel" },
		{ "Your game folder", "Ton dossier du jeu" },
		{ "Your ID", "Ton ID" },
		{ "Your own values from Advanced Tweaks: resolution, texture size, buffers, memory guards, FPS clock. Copy them as a code to share, or paste a friend's.", "Tes propres valeurs des Réglages avancés : résolution, taille des textures, buffers, protections mémoire, horloge FPS. Copie-les en code à partager, ou colle celui d'un ami." },
		{ "Your own values. They count when the Custom card is picked on the Play page. Lower values use less memory and give a softer picture.", "Tes propres valeurs. Elles comptent quand la carte Personnalisé est choisie dans la page Jouer. Des valeurs plus basses utilisent moins de mémoire et donnent une image plus douce." },
		{ "your values", "tes valeurs" },
		{ "★ edupode (recommended, every card: least video memory, most stable)", "★ edupode (recommandé, toutes les cartes : le moins de VRAM, le plus stable)" },
		{ "AMD alternative · VIctory-Belobug-AMD by Belobug", "Alternative AMD · VIctory-Belobug-AMD par Belobug" },
		{ "Every card plays with the edupode engine: video memory from ~12 GB down to 6-8 GB, ~4x more textures in it, the most stable. For Radeon cards there is also VIctory-Belobug-AMD, Belobug's fork tuned for Radeon (github.com/Belobug/VIctory-Belobug-AMD, GPL-3.0; all credit for it is Belobug's), which Setup builds once.", "Toutes les cartes jouent avec le moteur edupode : VRAM de ~12 Go ramenée à 6-8 Go, ~4 fois plus de textures dedans, le plus stable. Pour les cartes Radeon il y a aussi VIctory-Belobug-AMD, le fork de Belobug optimisé pour Radeon (github.com/Belobug/VIctory-Belobug-AMD, GPL-3.0 ; tout le mérite revient à Belobug), qu'Installation compile une fois." },
		{ "Send errors", "Envoyer les erreurs" },
		{ "Send the report", "Envoyer le rapport" },
		{ "Send to edupode", "Envoyer à edupode" },
		{ "Open a GitHub issue", "Ouvrir une issue GitHub" },
		{ "the package is too big to send; use the GitHub issue", "le paquet est trop gros pour être envoyé ; utilise l'issue GitHub" },
		{ "Sent to edupode: report #{0}.\r\n\r\nYour hardware ID is {1} (copied). Paste it on Discord, in \"{2}\", so edupode can find your reports.", "Envoyé à edupode : rapport n° {0}.\r\n\r\nTon ID matériel est {1} (copié). Colle-le sur Discord, dans \"{2}\", pour qu'edupode retrouve tes rapports." },
		{ "Too many reports this hour: try again later, or use the GitHub issue.", "Trop de rapports cette heure-ci : réessaie plus tard, ou utilise l'issue GitHub." },
		{ "Could not send the report: ", "Impossible d'envoyer le rapport : " },
		{ "\r\n\r\nOpen Discord now?", "\r\n\r\nOuvrir Discord maintenant ?" },
		{ "This game folder holds only {0:0.0} GB: the game's data is not in it (the whole dump is about 100 GB). Choose the folder of the complete dump, with all its files beside game_scarlett_master.exe.", "Ce dossier du jeu ne fait que {0:0.0} Go : les données du jeu n'y sont pas (le dump complet fait environ 100 Go). Choisis le dossier du dump complet, avec tous ses fichiers à côté de game_scarlett_master.exe." },
		{ "AMD alternative engine (Belobug)", "Moteur alternatif AMD (Belobug)" },
		{ "Only if you chose it in Hardware & ID: VIctory-Belobug-AMD, Belobug's fork for Radeon (GPL-3.0). Downloads its source and builds it once.", "Seulement si tu l'as choisi dans Matériel et ID : VIctory-Belobug-AMD, le fork de Belobug pour Radeon (GPL-3.0). Télécharge son code et le compile une fois." },
		{ "Send errors (GitHub / Discord)", "Envoyer les erreurs (GitHub / Discord)" },
		{ "Output 1080p (Full HD)", "Sortie 1080p (Full HD)" },
		{ "Output 1440p (2K)", "Sortie 1440p (2K)" },
		{ "Output 2160p (4K)", "Sortie 2160p (4K)" },
		{ "World at 720p (lighter)", "Monde en 720p (plus léger)" },
		{ "ReBAR n/a (card)", "ReBAR n/d (carte)" },
		{ "ReBAR not offered by Windows", "ReBAR non proposé par Windows" },
		{ "ReBAR ?", "ReBAR ?" },
		{ "The resolution the game draws its picture for; the preset's scale applies on top (Recommended at 4K draws 2560x1440 and upscales). Higher output: a sharper picture, more video memory for the screen's images (about +0.5 GB at 2K, +1.5 GB at 4K) and more work for the card; the CPU stays the limit on strong cards. 2K and 4K are new: tell us on Discord how they run.", "La résolution pour laquelle le jeu dessine son image ; l'échelle du préréglage s'applique par-dessus (Recommandé en 4K dessine en 2560x1440 puis agrandit). Sortie plus haute : image plus nette, plus de VRAM pour les images de l'écran (environ +0,5 Go en 2K, +1,5 Go en 4K) et plus de travail pour la carte ; sur les cartes puissantes le processeur reste la limite. 2K et 4K sont nouveaux : dis-nous sur Discord comment ça tourne." },
		{ "★ More CPU cores", "★ Plus de cœurs du processeur" },
		{ "Half the cores copy the game's memory (+1 FPS)", "La moitié des cœurs copie la mémoire du jeu (+1 FPS)" },
		{ "★ Tested on edupode's PC: +1 FPS in the open world (21.6 vs 20.5) and 2.3 ms less preparing each frame. The CPU is the limit (the card waits ~0 ms a frame): 6 helper threads used to refresh the game's buffers and textures (~18 ms of a 44 ms frame); this gives them half your cores (up to 12) and asks Windows which pages changed on a thread of its own.", "★ Testé sur le PC d'edupode : +1 FPS en monde ouvert (21,6 contre 20,5) et 2,3 ms de moins pour préparer chaque image. Le processeur est la limite (la carte attend ~0 ms par image) : 6 threads d'aide rafraîchissaient les buffers et les textures du jeu (~18 ms d'une image de 44 ms) ; ceci leur donne la moitié de tes cœurs (jusqu'à 12) et demande à Windows quelles pages ont changé sur un thread à part." },
		{ "The edupode engine ended at start on this Radeon (1 of 2): one more and the launcher moves to Belobug's AMD engine.", "Le moteur edupode s'est arrêté au démarrage sur cette Radeon (1 sur 2) : encore une fois et le launcher passe au moteur AMD de Belobug." },
		{ "The edupode engine ended at start twice on this Radeon: switched to Belobug's AMD engine.", "Le moteur edupode s'est arrêté deux fois au démarrage sur cette Radeon : passage au moteur AMD de Belobug." },
		{ "The edupode engine did not start twice in a row on your Radeon, so the launcher switched to Belobug's AMD engine (VIctory-Belobug-AMD, all credit to Belobug). If it is not built yet, Setup builds it now; then press PLAY.\r\n\r\nTo try the edupode engine again: Hardware & ID > Engine. A report ('Send errors') helps us make it start on your card.", "Le moteur edupode n'a pas démarré deux fois de suite sur ta Radeon, donc le launcher est passé au moteur AMD de Belobug (VIctory-Belobug-AMD, tout le mérite revient à Belobug). S'il n'est pas encore compilé, Installation le compile maintenant ; puis appuie sur JOUER.\r\n\r\nPour réessayer le moteur edupode : Matériel et ID > Moteur. Un rapport ('Envoyer les erreurs') nous aide à le faire démarrer sur ta carte." },
		{ "{0}×{1} internal upscaled to {2}p", "{0}×{1} interne agrandi en {2}p" },
		{ "native {0}p", "{0}p natif" },
		{ "Setup is still working (building VIctory or preparing shaders). PLAY opens when it finishes.", "Installation travaille encore (compilation de VIctory ou préparation des shaders). JOUER s'ouvre quand c'est fini." },
		{ "Stopped a preparation left running by another launcher window.", "Préparation arrêtée : une autre fenêtre du launcher l'avait laissée tourner." },
		{ "Windows Security: 'Access denied' when building", "Sécurité Windows : « Accès refusé » à la compilation" },
		{ "Only if a build from source ends with 'Access denied': Controlled folder access blocks freshly built programs. Opens Windows Security; allow victory.exe there yourself. The launcher never changes antivirus settings.", "Seulement si une compilation depuis le code se termine par « Accès refusé » : l'accès contrôlé aux dossiers bloque les programmes fraîchement compilés. Ouvre la Sécurité Windows ; autorise victory.exe toi-même. Le launcher ne modifie jamais les réglages de l'antivirus." },
		{ "Open it", "L'ouvrir" }
	};

	private Mode Selected
	{
		get
		{
			if (modeIndex < 0)
			{
				return modes[0];
			}
			return modes[modeIndex];
		}
	}

	private string VictoryExe
	{
		get
		{
			IEnumerable<string> enumerable = (UseAmdEngine ? new string[3]
			{
				Path.Combine(BuildTarget(AmdEngineDir), "diag", "victory.exe"),
				Path.Combine(AmdEngineDir, "target", "diag", "victory.exe"),
				Path.Combine(AmdEngineDir, "victory.exe")
			} : OwnExeCandidates);
			foreach (string item in enumerable)
			{
				if (File.Exists(item))
				{
					return item;
				}
			}
			return null;
		}
	}

	private IEnumerable<string> OwnExeCandidates
	{
		get
		{
			List<string> list = new List<string>();
			string sourceDir = SourceDir;
			if (sourceDir != null)
			{
				list.Add(Path.Combine(sourceDir, "target", "diag", "victory.exe"));
			}
			if (sourceDir != null && StorageRoot != null)
			{
				list.Add(Path.Combine(BuildTarget(sourceDir), "diag", "victory.exe"));
			}
			list.Add(Path.Combine(root, "target", "diag", "victory.exe"));
			list.Add(Path.Combine(root, "victory.exe"));
			list.Add(Path.Combine(AppDir, "victory.exe"));
			return (from p in list.Distinct(StringComparer.OrdinalIgnoreCase)
				orderby (!File.Exists(p)) ? DateTime.MinValue : File.GetLastWriteTimeUtc(p) descending
				select p).ToList();
		}
	}

	private string AppDir
	{
		get
		{
			if (File.Exists(Path.Combine(root, "victory.exe")) || File.Exists(Path.Combine(root, "unins000.exe")))
			{
				return root;
			}
			return Path.Combine(Path.GetDirectoryName(dataRoot), "app");
		}
	}

	private string OwnVictoryExe
	{
		get
		{
			foreach (string ownExeCandidate in OwnExeCandidates)
			{
				if (File.Exists(ownExeCandidate))
				{
					return ownExeCandidate;
				}
			}
			return null;
		}
	}

	private int OutputHeight
	{
		get
		{
			if (outputBox != null)
			{
				if (outputBox.SelectedIndex != 3)
				{
					if (outputBox.SelectedIndex != 2)
					{
						if (outputBox.SelectedIndex != 1)
						{
							return 1080;
						}
						return 1440;
					}
					return 2160;
				}
				return 720;
			}
			return 1080;
		}
	}

	private int OutputWidth => OutputHeight * 16 / 9;

	private int DisplayHeight
	{
		get
		{
			if (OutputHeight != 720)
			{
				return OutputHeight;
			}
			return 1080;
		}
	}

	private int DisplayWidth => DisplayHeight * 16 / 9;

	private static string NvidiaSmi
	{
		get
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
			if (!File.Exists(text))
			{
				return "nvidia-smi";
			}
			return text;
		}
	}

	private string SavesDir => Path.Combine(dataDir, "7952987A", "saves");

	private string SaveBackups => Path.Combine(Path.GetDirectoryName(dataRoot), "save-backups");

	private string CargoExe => FoundCargo() ?? "cargo";

	private bool NothingToBuild
	{
		get
		{
			if (VictoryExe == null)
			{
				if (!UseAmdEngine)
				{
					if (!File.Exists(Path.Combine(root, "victory.exe")))
					{
						return File.Exists(Path.Combine(AppDir, "victory.exe"));
					}
					return true;
				}
				return false;
			}
			return true;
		}
	}

	private string SourceDir
	{
		get
		{
			Func<string, bool> func = (string d) => d != null && File.Exists(Path.Combine(d, "Cargo.toml")) && File.Exists(Path.Combine(d, ".victory-root"));
			for (DirectoryInfo directoryInfo = new DirectoryInfo(root); directoryInfo != null; directoryInfo = directoryInfo.Parent)
			{
				if (func(directoryInfo.FullName))
				{
					if (sourceSeen != directoryInfo.FullName)
					{
						sourceSeen = directoryInfo.FullName;
						if (ReadSetting("source") != directoryInfo.FullName)
						{
							WriteSetting("source", directoryInfo.FullName);
						}
					}
					return directoryInfo.FullName;
				}
			}
			string text = ReadSetting("source");
			if (func(text))
			{
				return text;
			}
			string text2 = Path.Combine(Path.GetDirectoryName(dataRoot), "source", "cold-project-vi");
			if (!File.Exists(Path.Combine(text2, "Cargo.toml")))
			{
				return null;
			}
			return text2;
		}
	}

	private string GameExe
	{
		get
		{
			string text = gameBox.Text.Trim();
			string[] array = GameExes(text);
			string path;
			if (exeChoice != null && array.Contains(exeChoice, StringComparer.OrdinalIgnoreCase))
			{
				path = exeChoice;
			}
			else
			{
				path = ((array.Length > 0) ? array[0] : "game_scarlett_master.exe");
			}
			return Path.Combine(text, path);
		}
	}

	private string AmdEngineDir => Path.Combine(StorageRoot ?? Path.GetDirectoryName(dataRoot), "engines", "amd");

	private string EngineChoice
	{
		get
		{
			if (!(ReadSetting("engine") == "amd") || (hasNvidia && !hasRadeon))
			{
				return "edupode";
			}
			return "amd";
		}
	}

	private bool UseAmdEngine => EngineChoice == "amd";

	private string EngineName
	{
		get
		{
			if (!UseAmdEngine)
			{
				return "edupode";
			}
			return "AMD · Belobug";
		}
	}

	private string NvSmoothDll => Path.Combine(AppDir, "tools", "nvsmooth30", "version.dll");

	private bool NvSmoothHere
	{
		get
		{
			if (rtx30 && File.Exists(NvSmoothDll))
			{
				if (!(FgChoice == "auto"))
				{
					return FgChoice == "nvsmooth30";
				}
				return true;
			}
			return false;
		}
	}

	private bool OwnFrameGen
	{
		get
		{
			if (!rtxFrameGen && !NvSmoothHere && !UseAmdEngine)
			{
				if (!(FgChoice == "auto"))
				{
					return FgChoice == "vi";
				}
				return true;
			}
			return false;
		}
	}

	private string FgChoice
	{
		get
		{
			string text = ReadSetting("fg_engine");
			switch (text)
			{
			default:
				return "auto";
			case "smooth":
			case "nvsmooth30":
			case "vi":
				return text;
			}
		}
	}

	private bool FrameGenAvailable
	{
		get
		{
			if (!rtxFrameGen && !NvSmoothHere)
			{
				if (OwnFrameGen)
				{
					return !integratedOnly;
				}
				return false;
			}
			return true;
		}
	}

	private string ActiveCacheDir
	{
		get
		{
			if (!UseAmdEngine)
			{
				return cacheDir;
			}
			return Path.Combine(dataRoot, "cache-amd");
		}
	}

	private bool SessionLogsOn => ReadSetting("share_logs") == "1";

	private string StorageRoot
	{
		get
		{
			string text = ReadSetting("storage");
			if (!string.IsNullOrEmpty(text))
			{
				return text;
			}
			return null;
		}
	}

	private static string L(string en, string pt, string fr = null)
	{
		if (!Fr)
		{
			if (!Pt)
			{
				return en;
			}
			return pt;
		}
		return fr ?? Fr_(en);
	}

	private static void RestartLauncher()
	{
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo(Application.ExecutablePath, "--wait-pid " + Process.GetCurrentProcess().Id);
			processStartInfo.UseShellExecute = false;
			Process.Start(processStartInfo);
		}
		catch (Exception)
		{
		}
		Application.Exit();
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

	public LauncherForm()
	{
		settingsFile = Path.Combine(Path.GetDirectoryName(dataRoot), "launcher.ini");
		FormClosingEventHandler value = (object s, FormClosingEventArgs e) =>
		{
			if (!e.Cancel)
			{
				bool flag;
				lock (tools)
				{
					flag = tools.Any((Process t) =>
					{
						try
						{
							return !t.HasExited;
						}
						catch (Exception)
						{
							return false;
						}
					});
				}
				if (e.CloseReason == CloseReason.UserClosing && flag)
				{
					closePrompt = true;
				}
				else
				{
					closing = true;
				}
			}
		};
		FormClosing += value;
		FormClosed += (object s, FormClosedEventArgs e) =>
		{
			closing = true;
		};
		Activated += (object s, EventArgs e) =>
		{
			if (closePrompt && !closing)
			{
				try
				{
					BeginInvoke((Action)(() =>
					{
						if (!closing)
						{
							closePrompt = false;
						}
					}));
				}
				catch (Exception)
				{
				}
			}
		};
		Anim.Attach(this, ReadSetting("animations") != "0");
		Pt = ReadSetting("lang") == "pt";
		Fr = ReadSetting("lang") == "fr";
		cacheDir = ReadSetting("cache");
		if (cacheDir == null || (!cacheDir.All((char c) => c < '\u0080') && cacheDir == Path.Combine(dataRoot, "cache")))
		{
			cacheDir = DefaultCacheDir();
		}
		dataDir = ReadSetting("data") ?? Path.Combine(dataRoot, "data");
		modes = new Mode[7]
		{
			new Mode(L("Recommended", "Recomendado"), "720p → 1080p", L("Balanced · edupode", "Equilibrado · edupode"), L("The world drawn at 1280x720 and rebuilt to 1080p by the game's own upscaler, texture levels held (video memory under budget), 60 FPS clock. About 22-26 FPS in the open world and 35+ in cutscenes on an RTX 5070 Ti laptop (6-8 GB of video memory, ~20 GB of RAM).", "O mundo desenhado a 1280x720 e reconstruido para 1080p pelo upscaler do proprio jogo, niveis de texturas guardados (VRAM dentro do limite), relogio de 60 FPS. Cerca de 22-26 FPS no mundo aberto e 35+ nas cutscenes num portatil RTX 5070 Ti (6-8 GB de VRAM, ~20 GB de RAM)."), "0.6666667", light: true, modest: false),
			new Mode(L("Fastest", "Mais rapido"), "540p", L("Peak FPS", "Mais FPS"), L("The world drawn at 960x540: the most frames, a softer picture. For slower CPUs.", "O mundo desenhado a 960x540: mais frames, imagem mais suave. Para processadores mais lentos."), "0.5", light: true, modest: false),
			new Mode(L("Modest PC", "PC modesto"), "540p", L("6-8 GB GPUs", "Placas 6-8 GB"), L("For 6-8 GB cards and 16 GB of RAM: 540p, textures over 1024 texels halved, fewer of the game's buffers on the card, smaller texture batches, the memory guards on. Less video memory and RAM, a softer picture.", "Para placas de 6-8 GB e 16 GB de RAM: 540p, texturas acima de 1024 cortadas a metade, menos buffers do jogo na placa, lotes de texturas mais pequenos, protecoes de memoria ligadas. Menos VRAM e RAM, imagem mais suave."), "0.5", light: true, modest: true),
			new Mode(L("Sharp", "Nitido"), "1080p", L("12 GB+ GPUs", "Placas 12 GB+"), L("The world at full 1080p with held texture levels. Needs a strong GPU with 12 GB or more; slower in the open world.", "O mundo a 1080p completos com niveis de texturas guardados. Precisa de uma placa forte com 12 GB ou mais; mais lento no mundo aberto."), null, light: true, modest: false),
			new Mode(L("Classic cgbj", "cgbj classico"), "1080p", L("Reference", "Referencia"), L("The original cgbj settings at 1080p, no texture changes. For comparison.", "As definicoes originais do cgbj a 1080p, sem mudancas nas texturas. Para comparar."), null, light: false, modest: false),
			new Mode(L("Ultra Low", "Ultra baixo"), "540p", L("4-6 GB, 8-16 GB RAM", "4-6 GB, 8-16 GB RAM"), L("For the weakest PCs (4-6 GB cards, 8-16 GB of RAM): 540p, textures capped at 512 texels, the fewest buffers on the card, small texture batches, the memory guards on and a 30 FPS clock. The least memory of all; a soft picture. Give Windows a large page file (Setup).", "Para os PCs mais fracos (placas de 4-6 GB, 8-16 GB de RAM): 540p, texturas ate 512 texels, o minimo de buffers na placa, lotes de texturas pequenos, protecoes de memoria ligadas e relogio de 30 FPS. A menor memoria de todas; imagem suave. Da ao Windows um ficheiro de paginacao grande (Preparar)."), "0.5", light: true, modest: true)
			{
				UltraLow = true
			},
			new Mode(L("Custom", "Personalizado"), L("your values", "os teus valores"), L("Advanced Tweaks", "Ajustes avancados"), L("Your own values from Advanced Tweaks: resolution, texture size, buffers, memory guards, FPS clock. Copy them as a code to share, or paste a friend's.", "Os teus proprios valores dos Ajustes avancados: resolucao, tamanho das texturas, buffers, protecoes de memoria, relogio de FPS. Copia-os como um codigo para partilhar, ou cola o de um amigo."), null, light: true, modest: false)
			{
				Custom = true
			}
		};
		Text = "Julia's Launcher";
		try
		{
			Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		}
		catch (Exception)
		{
		}
		ClientSize = new Size(1200, 900);
		MinimumSize = new Size(1160, 720);
		BackColor = Theme.Back;
		ForeColor = Theme.Text;
		Font = Theme.Ui(9.75f);
		StartPosition = FormStartPosition.CenterScreen;
		DoubleBuffered = true;
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Theme.Back
		};
		Panel panel2 = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Theme.Back
		};
		panel.Controls.Add(panel2);
		panel.Controls.Add(Footer());
		pages.Add(PlayPage());
		pages.Add(MemoryPage());
		pages.Add(ShadersPage());
		pages.Add(ControlsPage());
		pages.Add(SetupPage());
		pages.Add(HardwarePage());
		pages.Add(UpdatesPage());
		pages.Add(HelpPage());
		pages.Add(CustomPage());
		foreach (Panel page in pages)
		{
			page.Dock = DockStyle.Fill;
			page.Visible = false;
			panel2.Controls.Add(page);
		}
		Controls.Add(panel);
		Controls.Add(SideBar());
		Controls.Add(TopBar());
		LoadSettings();
		DetectSystem();
		Show(0);
		Refresh2();
		if (VictoryExe == null || !File.Exists(GameExe))
		{
			Show(4);
		}
		string[] commandLineArgs = Environment.GetCommandLineArgs();
		for (int num = 1; num + 1 < commandLineArgs.Length; num++)
		{
			if (commandLineArgs[num] == "--page" && int.TryParse(commandLineArgs[num + 1], out var result) && result >= 0 && result < pages.Count)
			{
				Show(result);
			}
		}
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
		timer.Interval = 2000;
		System.Windows.Forms.Timer timer2 = timer;
		timer2.Tick += (object s, EventArgs a) =>
		{
			if (!setupBusy)
			{
				Refresh2();
			}
		};
		timer2.Start();
		if (!File.Exists(GameExe))
		{
			RunSteps(steps.Where((Step st) => st.Fix == new Func<bool>(FindGame)).ToArray());
		}
		else
		{
			CheckAll();
		}
		Thread thread = new Thread(() =>
		{
			string s = SmoothMotion.Status();
			try
			{
				BeginInvoke((Action)(() =>
				{
					smoothState = s;
					UpdateBanner();
					UpdateSmooth();
				}));
			}
			catch (Exception)
			{
			}
		});
		thread.IsBackground = true;
		thread.Start();
		ProbeGpu();
		EnsureAgility();
		StartChecks();
		System.Windows.Forms.Timer later = new System.Windows.Forms.Timer
		{
			Interval = 1500
		};
		later.Tick += (object s, EventArgs a) =>
		{
			later.Stop();
			later.Dispose();
			ShowWhatsNewOnce();
			CleanupOldLaunchers();
			if (!setupBusy)
			{
				ShowNotices();
			}
		};
		later.Start();
		StartUpdates();
		StartHardwareShare();
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		int value = 1;
		try
		{
			if (DwmSetWindowAttribute(Handle, 20, ref value, 4) != 0)
			{
				DwmSetWindowAttribute(Handle, 19, ref value, 4);
			}
		}
		catch (Exception)
		{
		}
		int value2 = Theme.Side.R | (Theme.Side.G << 8) | (Theme.Side.B << 16);
		try
		{
			DwmSetWindowAttribute(Handle, 35, ref value2, 4);
		}
		catch (Exception)
		{
		}
	}

	private Control TopBar()
	{
		Panel bar = new Panel
		{
			Dock = DockStyle.Top,
			Height = 52,
			BackColor = Theme.Side
		};
		string[][] linkText = new string[2][]
		{
			new string[2]
			{
				"Discord",
				L("Chat and help", "Conversa e ajuda", "Discussion et aide")
			},
			new string[2]
			{
				"Site",
				L("Source on GitHub", "Codigo no GitHub", "Code sur GitHub")
			}
		};
		string[] linkUrl = new string[2] { "https://discord.gg/648WEXCAsZ", "https://github.com/felicienradio/cold-project-vi/tree/main" };
		Rectangle[] linkBox = new Rectangle[2];
		int hot = -1;
		updateBadge = new Label
		{
			Text = "",
			AutoSize = true,
			ForeColor = Theme.Good,
			BackColor = Theme.Side,
			Font = Theme.Mono(8.5f),
			Cursor = Cursors.Hand
		};
		updateBadge.Click += (object s, EventArgs e) =>
		{
			Show(6);
		};
		bar.Controls.Add(updateBadge);
		Action place = () =>
		{
			int num = bar.Width - 28;
			using (Font f = Theme.Ui(8.5f))
			{
				for (int num2 = 1; num2 >= 0; num2--)
				{
					int num3 = Math.Max(Kit.TextW(linkText[num2][0], f), Kit.TextW(linkText[num2][1], f)) + 20;
					num -= num3;
					ref Rectangle reference = ref linkBox[num2];
					reference = new Rectangle(num, 9, num3, 34);
					num -= 30;
				}
			}
			updateBadge.Location = new Point(linkBox[0].Left - updateBadge.Width - 12, (bar.Height - updateBadge.Height) / 2);
		};
		bar.Paint += (object s, PaintEventArgs e) =>
		{
			Graphics graphics = e.Graphics;
			Theme.Smooth(graphics);
			RectangleF rectangleF = new RectangleF(16f, 13f, 26f, 26f);
			using (GraphicsPath path = Theme.Round(rectangleF, 7f))
			{
				using SolidBrush brush = new SolidBrush(Theme.Cyan);
				graphics.FillPath(brush, path);
			}
			using (Font font = new Font("Segoe UI Black", 8.5f))
			{
				using SolidBrush brush2 = new SolidBrush(Theme.OnAccent);
				using StringFormat format = new StringFormat
				{
					Alignment = StringAlignment.Center,
					LineAlignment = StringAlignment.Center
				};
				graphics.DrawString("VI", font, brush2, rectangleF, format);
			}
			Theme.Meta(graphics, "Julia's Launcher", "v0.3.16", Theme.Muted, new RectangleF(54f, 9f, 240f, 34f), StringAlignment.Near);
			for (int i = 0; i < 2; i++)
			{
				Theme.Meta(graphics, linkText[i][0] + " ↗", linkText[i][1], (i == hot) ? Theme.Soft : Theme.Muted, new RectangleF(linkBox[i].X, linkBox[i].Y, linkBox[i].Width + 14, 34f), StringAlignment.Near);
			}
			using Pen pen = new Pen(Theme.Rule);
			graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
		};
		bar.MouseMove += (object s, MouseEventArgs e) =>
		{
			int num = -1;
			for (int i = 0; i < 2; i++)
			{
				if (linkBox[i].Contains(e.Location))
				{
					num = i;
				}
			}
			if (num != hot)
			{
				hot = num;
				bar.Cursor = ((num >= 0) ? Cursors.Hand : Cursors.Default);
				bar.Invalidate();
			}
		};
		bar.MouseLeave += (object s, EventArgs e) =>
		{
			if (hot != -1)
			{
				hot = -1;
				bar.Cursor = Cursors.Default;
				bar.Invalidate();
			}
		};
		bar.MouseClick += (object s, MouseEventArgs e) =>
		{
			for (int i = 0; i < 2; i++)
			{
				if (linkBox[i].Contains(e.Location))
				{
					OpenUrl(linkUrl[i]);
				}
			}
		};
		bar.Resize += (object s, EventArgs e) =>
		{
			place();
			bar.Invalidate();
		};
		place();
		return bar;
	}

	private Control SideBar()
	{
		Panel side = new Panel
		{
			Dock = DockStyle.Left,
			Width = 216,
			BackColor = Theme.Side
		};
		side.Paint += (object s, PaintEventArgs e) =>
		{
			using Pen pen = new Pen(Theme.Rule);
			e.Graphics.DrawLine(pen, side.Width - 1, 0, side.Width - 1, side.Height);
		};
		string[][] array = new string[8][]
		{
			new string[2]
			{
				"\ue768",
				L("Play", "Jogar")
			},
			new string[2]
			{
				"\ue9d9",
				L("Performance", "Desempenho")
			},
			new string[2]
			{
				"\ue790",
				L("Shaders", "Shaders")
			},
			new string[2]
			{
				"\ue7fc",
				L("Controls", "Controlos")
			},
			new string[2]
			{
				"\ue713",
				L("Setup", "Preparar")
			},
			new string[2]
			{
				"\ue7f8",
				L("Hardware & ID", "Hardware e ID")
			},
			new string[2]
			{
				"\ue895",
				L("Updates", "Atualizacoes")
			},
			new string[2]
			{
				"\ue897",
				L("Help", "Ajuda")
			}
		};
		for (int num = 0; num < array.Length; num++)
		{
			int k = num;
			NavButton navButton = new NavButton();
			navButton.Glyph = (num + 1).ToString("00");
			navButton.Text = array[num][1];
			navButton.Size = new Size(200, 38);
			navButton.Location = new Point(8, 24 + num * 40);
			NavButton navButton2 = navButton;
			navButton2.Click += (object s, EventArgs e) =>
			{
				Show(k);
			};
			nav.Add(navButton2);
			side.Controls.Add(navButton2);
		}
		Pill report = new Pill
		{
			Text = L("Send errors (GitHub / Discord)", "Enviar erros (GitHub / Discord)"),
			Size = new Size(184, 34),
			Location = new Point(16, 0),
			BackColor = Theme.Side,
			Anchor = (AnchorStyles.Bottom | AnchorStyles.Left)
		};
		report.Click += (object s, EventArgs e) =>
		{
			ReportErrors();
		};
		side.Controls.Add(report);
		Pill en = new Pill
		{
			Link = true,
			Text = "EN",
			Size = new Size(58, 30),
			BackColor = Theme.Side,
			Anchor = (AnchorStyles.Bottom | AnchorStyles.Left),
			Main = (!Pt && !Fr)
		};
		Pill pt = new Pill
		{
			Link = true,
			Text = "PT",
			Size = new Size(58, 30),
			BackColor = Theme.Side,
			Anchor = (AnchorStyles.Bottom | AnchorStyles.Left),
			Main = Pt
		};
		Pill fr = new Pill
		{
			Link = true,
			Text = "FR",
			Size = new Size(58, 30),
			BackColor = Theme.Side,
			Anchor = (AnchorStyles.Bottom | AnchorStyles.Left),
			Main = Fr
		};
		en.Click += (object s, EventArgs e) =>
		{
			if (Pt || Fr)
			{
				Pt = false;
				Fr = false;
				SaveSettings();
				RestartLauncher();
			}
		};
		pt.Click += (object s, EventArgs e) =>
		{
			if (!Pt)
			{
				Pt = true;
				Fr = false;
				SaveSettings();
				RestartLauncher();
			}
		};
		fr.Click += (object s, EventArgs e) =>
		{
			if (!Fr)
			{
				Fr = true;
				Pt = false;
				SaveSettings();
				RestartLauncher();
			}
		};
		side.Controls.Add(en);
		side.Controls.Add(pt);
		side.Controls.Add(fr);
		Label tech = new Label
		{
			Text = "D3D12 · SM 6.6",
			Font = Theme.Mono(8f),
			ForeColor = Theme.Dim,
			AutoSize = true,
			BackColor = Theme.Side,
			Anchor = (AnchorStyles.Bottom | AnchorStyles.Left)
		};
		side.Controls.Add(tech);
		side.Resize += (object s, EventArgs e) =>
		{
			report.Top = side.Height - 124;
			en.Location = new Point(16, side.Height - 80);
			pt.Location = new Point(79, side.Height - 80);
			fr.Location = new Point(142, side.Height - 80);
			tech.Location = new Point((side.Width - tech.Width) / 2, side.Height - 36);
		};
		return side;
	}

	private void Show(int page)
	{
		bool flag = page != shownPage;
		shownPage = page;
		for (int i = 0; i < pages.Count; i++)
		{
			pages[i].Visible = i == page;
		}
		if (flag)
		{
			foreach (Control control in pages[page].Controls)
			{
				if (control is Poster poster)
				{
					poster.PlayIntro();
					break;
				}
			}
		}
		Anim.Wake();
		for (int j = 0; j < nav.Count; j++)
		{
			nav[j].Active = j == page || (page == 8 && j == 0);
		}
		PlacePlayButton(page);
		if (page == 4)
		{
			CheckAll();
		}
		if (page == 1)
		{
			ScanMemory();
		}
		if (page == 2)
		{
			RefreshShaders();
		}
		if (page == 5)
		{
			RefreshHardware();
		}
		if (page == 6)
		{
			RefreshUpdates();
		}
		if (page == 8)
		{
			RefreshCustom();
		}
	}

	private Panel Page()
	{
		Panel page = new Panel
		{
			BackColor = Theme.Back,
			AutoScroll = true,
			Padding = new Padding(24, 20, 24, 20)
		};
		Action stretch = () =>
		{
			int num = page.ClientSize.Width;
			int num2 = page.AutoScrollPosition.X;
			if (num <= 0)
			{
				return;
			}
			foreach (Control control in page.Controls)
			{
				if (control is Poster && (control.Width != num || control.Left != num2))
				{
					control.SetBounds(num2, 0, num, 0, BoundsSpecified.X | BoundsSpecified.Width);
				}
			}
		};
		page.Resize += (object s, EventArgs e) =>
		{
			stretch();
		};
		page.ClientSizeChanged += (object s, EventArgs e) =>
		{
			stretch();
		};
		page.ControlAdded += (object s, ControlEventArgs e) =>
		{
			stretch();
		};
		page.VisibleChanged += (object s, EventArgs e) =>
		{
			if (page.Visible)
			{
				stretch();
			}
		};
		return page;
	}

	private PageHead Head(string eyeValue, string heavy, string accent, string sub)
	{
		PageHead pageHead = new PageHead();
		pageHead.Location = new Point(0, 0);
		pageHead.Eye = "Julia's Launcher";
		pageHead.EyeValue = eyeValue;
		pageHead.Title = heavy;
		pageHead.Accent = accent;
		pageHead.Sub = sub;
		PageHead pageHead2 = pageHead;
		using Font f = Theme.Ui(10f);
		pageHead2.Height = 150 + ((!string.IsNullOrEmpty(sub)) ? (Kit.TextH(sub, f, pageHead2.SubWidth) + 8) : 0);
		return pageHead2;
	}

	private int PageTop(Panel page, string eyeValue, string heavy, string accent, string sub)
	{
		PageHead pageHead = Head(eyeValue, heavy, accent, sub);
		page.Controls.Add(pageHead);
		return pageHead.Height + 16;
	}

	private Label Small(string text, int x, int y, int width)
	{
		Label label = new Label();
		label.Text = text;
		label.Location = new Point(x, y);
		label.Size = new Size(width, 20);
		label.ForeColor = Theme.Dim;
		label.BackColor = Theme.Back;
		return label;
	}

	private Control Footer()
	{
		Panel foot = new Panel
		{
			Dock = DockStyle.Bottom,
			Height = 64,
			BackColor = Theme.Back
		};
		footPanel = foot;
		footMeta = new FootMeta
		{
			Location = new Point(0, 1),
			Size = new Size(400, 62),
			Labels = new string[5]
			{
				L("State", "Estado", "État"),
				L("Preset", "Predefinicao", "Préréglage"),
				L("Engine", "Motor", "Moteur"),
				L("Hardware", "Hardware", "Matériel"),
				L("Next step", "Proximo passo", "Prochaine étape")
			}
		};
		foot.Controls.Add(footMeta);
		closeButton = new Pill
		{
			Text = L("Close game", "Fechar o jogo"),
			Size = new Size(160, 44),
			Visible = false
		};
		closeButton.Click += (object s, EventArgs e) =>
		{
			if (State() != GameState.Running || MessageBox.Show(this, L("Close the game now? What was not saved is lost.", "Fechar o jogo agora? O que nao foi guardado perde-se."), "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				CloseGames();
				Refresh2();
			}
		};
		foot.Controls.Add(closeButton);
		playButton = new Pill
		{
			Main = true,
			Shine = true,
			Text = L("Play  →", "Jogar  →", "Jouer  →"),
			Size = new Size(190, 44),
			Font = new Font("Segoe UI Semibold", 12f)
		};
		playButton.Click += (object s, EventArgs e) =>
		{
			Play();
		};
		foot.Controls.Add(playButton);
		foot.Paint += (object s, PaintEventArgs e) =>
		{
			using Pen pen = new Pen(Theme.Rule);
			e.Graphics.DrawLine(pen, 0, 0, foot.Width, 0);
		};
		layoutFoot = () =>
		{
			int num = foot.Width - 40;
			if (playButton.Parent == foot)
			{
				playButton.Location = new Point(num - playButton.Width, 10);
				num = playButton.Left - 12;
			}
			if (closeButton.Visible)
			{
				closeButton.Location = new Point(num - closeButton.Width, 10);
				num = closeButton.Left - 12;
			}
			footMeta.Width = Math.Max(100, num - 8);
		};
		foot.Resize += (object s, EventArgs e) =>
		{
			layoutFoot();
		};
		closeButton.VisibleChanged += (object s, EventArgs e) =>
		{
			layoutFoot();
		};
		return foot;
	}

	private void PlacePlayButton(int page)
	{
		if (banner == null || footPanel == null)
		{
			return;
		}
		if (page == 0)
		{
			if (playButton.Parent != banner)
			{
				playButton.Size = new Size(200, 48);
				playButton.Font = new Font("Segoe UI Semibold", 12.5f);
				banner.Controls.Add(playButton);
			}
			playButton.Location = new Point(40, 256);
		}
		else if (playButton.Parent != footPanel)
		{
			playButton.Size = new Size(190, 44);
			playButton.Font = new Font("Segoe UI Semibold", 12f);
			footPanel.Controls.Add(playButton);
		}
		if (layoutFoot != null)
		{
			layoutFoot();
		}
	}

	private static void Rounded(Control c, float radius)
	{
		Action set = () =>
		{
			if (c.Width < 4 || c.Height < 4)
			{
				return;
			}
			using GraphicsPath path = Theme.Round(new RectangleF(0f, 0f, c.Width, c.Height), radius);
			c.Region = new Region(path);
		};
		c.Resize += (object s, EventArgs e) =>
		{
			set();
		};
		set();
	}

	private Field FieldFor(TextBox tb, int x, int y, int w, int h)
	{
		return FieldFor(tb, x, y, w, h, flat: false);
	}

	private Field FieldFor(TextBox tb, int x, int y, int w, int h, bool flat)
	{
		Field f = new Field
		{
			Location = new Point(x, y),
			Size = new Size(w, h)
		};
		tb.BorderStyle = BorderStyle.None;
		tb.BackColor = (flat ? Theme.Back : Theme.Card);
		f.Controls.Add(tb);
		Action lay = () =>
		{
			if (tb.Multiline)
			{
				tb.SetBounds(flat ? 2 : 12, 10, Math.Max(10, f.Width - (flat ? 4 : 24)), Math.Max(10, f.Height - 20));
			}
			else if (flat)
			{
				tb.SetBounds(0, Math.Max(0, (f.Height - tb.Height) / 2 - 1), Math.Max(10, f.Width - 4), tb.Height);
			}
			else
			{
				tb.SetBounds(14, Math.Max(0, (f.Height - tb.Height) / 2), Math.Max(10, f.Width - 28), tb.Height);
			}
		};
		f.Paint += (object s, PaintEventArgs e) =>
		{
			Theme.Smooth(e.Graphics);
			if (flat && tb.Multiline)
			{
				using (SolidBrush brush = new SolidBrush(Theme.Rule))
				{
					e.Graphics.FillRectangle(brush, 0, 0, f.Width, 1);
				}
				using SolidBrush brush2 = new SolidBrush(tb.Focused ? Theme.Cyan : Theme.Rule);
				e.Graphics.FillRectangle(brush2, 0, f.Height - ((!tb.Focused) ? 1 : 2), f.Width, (!tb.Focused) ? 1 : 2);
				return;
			}
			if (flat)
			{
				using (SolidBrush brush3 = new SolidBrush(tb.Focused ? Theme.Cyan : Theme.LineStrong))
				{
					e.Graphics.FillRectangle(brush3, 0, f.Height - ((!tb.Focused) ? 1 : 2), f.Width, (!tb.Focused) ? 1 : 2);
					return;
				}
			}
			Theme.Panel(e.Graphics, new RectangleF(0.5f, 0.5f, (float)f.Width - 1.5f, (float)f.Height - 1.5f), Theme.Card, tb.Focused ? Theme.Cyan : Theme.Line, tb.Multiline ? 10f : ((float)f.Height / 2f));
		};
		f.Resize += (object s, EventArgs e) =>
		{
			lay();
		};
		tb.GotFocus += (object s, EventArgs e) =>
		{
			f.Invalidate();
		};
		tb.LostFocus += (object s, EventArgs e) =>
		{
			f.Invalidate();
		};
		lay();
		return f;
	}

	private static void ThemeCombo(ComboBox c)
	{
		c.DrawMode = DrawMode.OwnerDrawFixed;
		c.ItemHeight = 22;
		c.DrawItem += (object s, DrawItemEventArgs e) =>
		{
			if (e.Index >= 0)
			{
				using (SolidBrush brush = new SolidBrush(((e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0) ? Theme.Selected : Theme.Card))
				{
					e.Graphics.FillRectangle(brush, e.Bounds);
				}
				TextRenderer.DrawText(e.Graphics, c.GetItemText(c.Items[e.Index]), c.Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
			}
		};
	}

	private void Fluid(Panel page, int x, int baseW, Action<int> after)
	{
		List<Tuple<Control, int, int>> snap = new List<Tuple<Control, int, int>>();
		foreach (Control control in page.Controls)
		{
			if (!(control is Poster))
			{
				snap.Add(Tuple.Create(control, control.Left, control.Width));
			}
		}
		bool busy = false;
		Action apply = () =>
		{
			if (busy || page.ClientSize.Width <= 0)
			{
				return;
			}
			busy = true;
			try
			{
				int num = page.ClientSize.Width - 2 * x;
				float num2 = Math.Max(0.8f, Math.Min(1.35f, (float)num / (float)baseW));
				int num3 = page.AutoScrollPosition.X;
				page.SuspendLayout();
				foreach (Tuple<Control, int, int> item in snap)
				{
					item.Item1.SetBounds(num3 + x + (int)Math.Round((float)(item.Item2 - x) * num2), 0, (int)Math.Round((float)item.Item3 * num2), 0, BoundsSpecified.X | BoundsSpecified.Width);
				}
				if (after != null)
				{
					after((int)Math.Round((float)baseW * num2));
				}
				page.ResumeLayout(performLayout: true);
			}
			finally
			{
				busy = false;
			}
		};
		page.Resize += (object s, EventArgs e) =>
		{
			apply();
		};
		page.VisibleChanged += (object s, EventArgs e) =>
		{
			if (page.Visible)
			{
				apply();
			}
		};
	}

	private Panel PlayPage()
	{
		Panel page = Page();
		Theme.TestedText = L("Tested", "Testado", "Testé");
		statusStrip = new StatusStrip();
		banner = new Hero
		{
			Location = new Point(0, 0),
			Size = new Size(980, 362),
			Brand = "Julia's Launcher",
			BrandValue = "v0.3.16",
			RightLabel = L("Build", "Versao", "Version"),
			RightValue = L("Xbox Series, run through VIctory", "Xbox Series, corrida pelo VIctory", "Xbox Series, lancée par VIctory"),
			Title = L("Ready", "Pronto", "Prêt"),
			Accent = L("when you are.", "quando quiseres.", "quand tu veux."),
			StepNames = new string[4]
			{
				L("Game", "Jogo", "Jeu"),
				L("Engine", "Motor", "Moteur"),
				L("Shaders", "Shaders", "Shaders"),
				"Windows"
			}
		};
		playButton.MouseDown += (object s, MouseEventArgs e) =>
		{
			if (playButton.Parent == banner && playButton.Enabled)
			{
				banner.Ripple();
			}
		};
		heroGhost = new Pill
		{
			Text = L("Setup", "Preparar", "Préparer"),
			Font = Theme.Ui(10.5f),
			Size = new Size(Kit.PillW(L("Fix it", "Corrigir", "Corriger"), 10.5f, 128), 48),
			Location = new Point(252, 256)
		};
		heroGhost.Click += (object s, EventArgs e) =>
		{
			Show(4);
		};
		heroAuto = new Pill
		{
			Link = true,
			Text = "Auto-Setup",
			Font = Theme.Ui(10f),
			Size = new Size(104, 32),
			Location = new Point(252 + heroGhost.Width + 10, 264)
		};
		heroAuto.Click += (object s, EventArgs e) =>
		{
			Show(4);
			DoEverything();
		};
		banner.Controls.Add(heroGhost);
		banner.Controls.Add(heroAuto);
		page.Controls.Add(banner);
		warnPanel = new Panel();
		warnText = new Label();
		warnFix = new Pill();
		warnFix.Click += (object s, EventArgs e) =>
		{
			Show(4);
		};
		int y = 390;
		gameHeading = new Heading(L("Game", "Jogo", "Jeu"))
		{
			Hair = false
		};
		page.Controls.Add(new Label
		{
			Text = L("Game", "Jogo", "Jeu"),
			Font = Theme.UiBold(10.5f),
			ForeColor = Theme.Text,
			BackColor = Theme.Back,
			UseMnemonic = false,
			Location = new Point(40, y + 7),
			Size = new Size(140, 22)
		});
		gameBox = new TextBox
		{
			ForeColor = Theme.Text,
			Font = Theme.Mono(9.5f)
		};
		gameBox.TextChanged += (object s, EventArgs e) =>
		{
			Refresh2();
		};
		page.Controls.Add(FieldFor(gameBox, 190, y, 496, 34, flat: true));
		Pill pill = new Pill();
		pill.Text = L("Auto-detect", "Detetar", "Détecter");
		pill.Location = new Point(702, y + 1);
		pill.Size = new Size(120, 32);
		Pill pill2 = pill;
		pill2.Click += (object s, EventArgs e) =>
		{
			RunSteps(steps.Where((Step st) => st.Fix == new Func<bool>(FindGame)).ToArray());
		};
		page.Controls.Add(pill2);
		Pill pill3 = new Pill();
		pill3.Text = L("Browse...", "Procurar...", "Parcourir...");
		pill3.Location = new Point(830, y + 1);
		pill3.Size = new Size(110, 32);
		Pill pill4 = pill3;
		pill4.Click += (object s, EventArgs e) =>
		{
			using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
			{
				Description = L("Choose the folder that holds the game's game_scarlett*.exe", "Escolhe a pasta que tem o game_scarlett*.exe do jogo")
			};
			if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
			{
				gameBox.Text = folderBrowserDialog.SelectedPath;
			}
		};
		page.Controls.Add(pill4);
		gameStatus = new Label
		{
			Location = new Point(190, y + 42),
			Size = new Size(470, 20),
			ForeColor = Theme.Dim,
			BackColor = Theme.Back,
			Font = Theme.Ui(9f)
		};
		page.Controls.Add(gameStatus);
		page.Controls.Add(ExeBox(700, y + 40, 240));
		y += 74;
		page.Controls.Add(new Rule
		{
			Location = new Point(40, y),
			Width = 900
		});
		y += 14;
		page.Controls.Add(new Label
		{
			Text = L("Engine", "Motor", "Moteur"),
			Font = Theme.UiBold(10.5f),
			ForeColor = Theme.Text,
			BackColor = Theme.Back,
			UseMnemonic = false,
			Location = new Point(40, y + 5),
			Size = new Size(140, 22)
		});
		string text = L("Standard · recommended", "Padrao · recomendado", "Standard · recommandé");
		string text2 = L("Belobug AMD · for Radeon", "Belobug AMD · para Radeon", "Belobug AMD · pour Radeon");
		int num;
		int num2;
		using (Font f = Theme.UiBold(10.5f))
		{
			num = Kit.TextW(text, f) + 28;
			num2 = Kit.TextW(text2, f) + 28;
		}
		Pill stdEngine = new Pill
		{
			Link = true,
			Text = text,
			Font = Theme.Ui(10.5f),
			Location = new Point(190, y),
			Size = new Size(num, 32)
		};
		Pill amdEngine = new Pill
		{
			Link = true,
			Text = text2,
			Font = Theme.Ui(10.5f),
			Location = new Point(190 + num + 12, y),
			Size = new Size(num2, 32)
		};
		Label engineNote = new Label
		{
			Location = new Point(190, y + 36),
			Size = new Size(750, 20),
			ForeColor = Theme.Muted,
			BackColor = Theme.Back,
			Font = Theme.Ui(8.75f),
			AutoEllipsis = true
		};
		showEnginePills = () =>
		{
			stdEngine.Main = !UseAmdEngine;
			amdEngine.Main = UseAmdEngine;
			stdEngine.Invalidate();
			amdEngine.Invalidate();
			engineNote.Text = (UseAmdEngine ? L("Belobug's fork for Radeon cards, built from its source.", "O fork do Belobug para placas Radeon, compilado do codigo.", "Le fork de Belobug pour les cartes Radeon, compilé depuis son code.") : L("On a Radeon card: if the game closes with \"device removed\", try Belobug AMD.", "Numa placa Radeon: se o jogo fechar com \"device removed\", experimenta o Belobug AMD.", "Sur une carte Radeon : si le jeu se ferme avec « device removed », essaie Belobug AMD."));
		};
		Action<bool> pickEngine = (bool amd) =>
		{
			if (amd != UseAmdEngine)
			{
				if (engineBox != null)
				{
					engineBox.SelectedIndex = (amd ? 1 : 0);
				}
				else
				{
					WriteSetting("engine", amd ? "amd" : "edupode");
					Log(L("Engine: ", "Motor: ") + EngineName);
					UpdateBanner();
					Refresh2();
				}
				showEnginePills();
			}
		};
		stdEngine.Click += (object s, EventArgs e) =>
		{
			pickEngine(obj: false);
		};
		amdEngine.Click += (object s, EventArgs e) =>
		{
			pickEngine(obj: true);
		};
		page.Controls.Add(stdEngine);
		page.Controls.Add(amdEngine);
		page.Controls.Add(engineNote);
		showEnginePills();
		y += 66;
		page.Controls.Add(new Rule
		{
			Location = new Point(40, y),
			Width = 900
		});
		y += 30;
		presetHeading = new Heading(L("Quality preset", "Predefinicao de imagem", "Préréglage d'image"))
		{
			Location = new Point(40, y),
			Width = 660,
			Hair = false
		};
		page.Controls.Add(presetHeading);
		outputBox = new Dropdown
		{
			Location = new Point(720, y - 3),
			Width = 220
		};
		outputBox.Items.AddRange(new object[4]
		{
			L("Output 1080p (Full HD)", "Saida 1080p (Full HD)"),
			L("Output 1440p (2K)", "Saida 1440p (2K)"),
			L("Output 2160p (4K)", "Saida 2160p (4K)"),
			L("World at 720p (lighter)", "Mundo a 720p (mais leve)")
		});
		outputBox.SelectedIndex = 0;
		outputBox.SelectedIndexChanged += (object s, EventArgs e) =>
		{
			SaveSettings();
			Refresh2();
		};
		ToolTip toolTip = new ToolTip();
		toolTip.AutoPopDelay = 20000;
		toolTip.SetToolTip(outputBox, L("The resolution the game draws its picture for; the preset's scale applies on top (Recommended at 4K draws 2560x1440 and upscales). Higher output: a sharper picture, more video memory for the screen's images (about +0.5 GB at 2K, +1.5 GB at 4K) and more work for the card; the CPU stays the limit on strong cards. 2K and 4K are new: tell us on Discord how they run.", "A resolucao para a qual o jogo desenha a imagem; a escala do preset aplica-se por cima (o Recomendado em 4K desenha 2560x1440 e amplia). Saida maior: imagem mais nitida, mais VRAM para as imagens do ecra (cerca de +0,5 GB em 2K, +1,5 GB em 4K) e mais trabalho para a placa; em placas fortes o limite continua a ser o processador. 2K e 4K sao novos: conta-nos no Discord como correm."));
		page.Controls.Add(outputBox);
		y += 42;
		for (int num3 = 0; num3 < CardOrder.Length; num3++)
		{
			int k = CardOrder[num3];
			int num4 = (int)Math.Round((double)(num3 * 912) / 7.0);
			int num5 = (int)Math.Round((double)((num3 + 1) * 912) / 7.0) - 12;
			ModeCard modeCard = new ModeCard();
			modeCard.Title = modes[k].Name;
			modeCard.Tag2 = modes[k].Tag;
			modeCard.Line = modes[k].Short;
			modeCard.Location = new Point(40 + num4, y);
			modeCard.Size = new Size(num5 - num4, 78);
			modeCard.Tag = k;
			ModeCard modeCard2 = modeCard;
			modeCard2.Click += (object s, EventArgs e) =>
			{
				SelectMode(k);
				if (k == 6)
				{
					Show(8);
				}
			};
			cards.Add(modeCard2);
			page.Controls.Add(modeCard2);
		}
		y += 90;
		modeInfo = new Label
		{
			Location = new Point(40, y),
			Size = new Size(780, 52),
			ForeColor = Theme.Soft,
			BackColor = Theme.Back,
			Font = Theme.Ui(9f)
		};
		page.Controls.Add(modeInfo);
		y += 66;
		page.Controls.Add(new Heading(L("Launch options", "Opcoes de arranque", "Options de lancement"))
		{
			Location = new Point(40, y),
			Width = 530,
			Hair = false
		});
		Pill pill5 = new Pill();
		pill5.Text = L("Reset to recommended", "Repor o recomendado", "Revenir au recommandé");
		pill5.Location = new Point(586, y - 3);
		pill5.Size = new Size(200, 32);
		Pill pill6 = pill5;
		pill6.Click += (object s, EventArgs e) =>
		{
			Recommended();
		};
		page.Controls.Add(pill6);
		Pill pill7 = new Pill();
		pill7.Text = L("Advanced Tweaks", "Ajustes avancados", "Réglages avancés");
		pill7.Location = new Point(794, y - 3);
		pill7.Size = new Size(146, 32);
		Pill pill8 = pill7;
		pill8.Click += (object s, EventArgs e) =>
		{
			Show(8);
		};
		page.Controls.Add(pill8);
		y += 44;
		Func<int, int> colLeft = (int c0) => (int)Math.Round((double)(c0 * 928) / 3.0);
		string[] array = new string[3]
		{
			L("Display", "Imagem", "Affichage"),
			L("Performance", "Desempenho", "Performances"),
			L("Picture fixes", "Correcoes de imagem", "Corrections d'image")
		};
		for (int num6 = 0; num6 < array.Length; num6++)
		{
			page.Controls.Add(new Eyebrow(array[num6])
			{
				Location = new Point(40 + colLeft(num6), y),
				Width = colLeft(num6 + 1) - colLeft(num6) - 28
			});
		}
		y += 26;
		Func<string, string, int, int, bool, CheckBox> func = (string title, string line, int col, int row, bool on) =>
		{
			OptionCard optionCard = new OptionCard
			{
				Text = title,
				Line = line,
				Checked = on,
				Location = new Point(40 + colLeft(col), y + row * 70),
				Size = new Size(colLeft(col + 1) - colLeft(col) - 28, 70)
			};
			optionCard.CheckedChanged += (object s, EventArgs e) =>
			{
				Refresh2();
			};
			page.Controls.Add(optionCard);
			return optionCard;
		};
		borderlessBox = func(L("★ Borderless Fullscreen", "★ Ecra inteiro sem bordas"), L("Native desktop presentation", "Apresentacao nativa no ambiente de trabalho"), 0, 0, arg5: true);
		fps60Box = func(L("★ Unlock 60 FPS Clock", "★ Desbloquear relogio de 60 FPS"), L("Decoupled simulation tick (no 30 FPS cap)", "Tick de simulacao separado (sem limite de 30 FPS)"), 0, 1, arg5: true);
		panelBox = func(L("★ Diagnostics Overlay (F3)", "★ Overlay de diagnostico (F3)"), L("FPS, frame time, VRAM & RAM", "FPS, tempo de frame, VRAM e RAM"), 0, 2, arg5: true);
		smoothBox = func(L("Frame generation", "Geracao de frames"), FrameGenLine(), 0, 3, arg5: true);
		reserveBox = func(L("Keep the most video memory", "Guardar o maximo de VRAM"), L("Windows reserves video memory for the game", "O Windows reserva VRAM para o jogo"), 1, 0, arg5: true);
		cpuBox = func(L("★ More CPU cores", "★ Mais nucleos do processador"), L("Half the cores copy the game's memory (+1 FPS)", "Metade dos nucleos copia a memoria do jogo (+1 FPS)"), 1, 1, arg5: true);
		warmBox = func(L("Load pipelines before the game", "Carregar pipelines antes do jogo"), L("No stutter the first time you see a place", "Sem engasgos a primeira vez que ves um sitio"), 1, 2, arg5: true);
		fightsBox = func(L("★ Crowds and fights", "★ Multidoes e lutas"), L("2 GB of the game's buffers on the card", "2 GB dos buffers do jogo na placa"), 1, 3, arg5: true);
		qualityBox = func(L("Anti-stretch", "Anti-esticados"), L("Fewer stretched bodies; several FPS slower", "Menos corpos esticados; varios FPS mais lento"), 2, 3, arg5: false);
		qualityBox.Visible = false;
		facesBox = func(L("Rainbow-face fix", "Correcao das caras arco-iris"), L("Textures finish uploading first; costs FPS", "Texturas acabam de subir primeiro; custa FPS"), 2, 0, arg5: false);
		loadingBox = func(L("Fast loading (experimental)", "Carregamento rapido (experimental)"), L("Shorter loading pauses; untested", "Pausas de carregamento mais curtas; nao testado"), 2, 1, arg5: false);
		y += 294;
		page.Controls.Add(new Label
		{
			Text = L("Frame generation engine", "Motor de geracao de frames", "Moteur de génération d'images"),
			Location = new Point(40, y + 6),
			AutoSize = true,
			ForeColor = Theme.Soft,
			BackColor = Theme.Back,
			Font = Theme.Ui(9.5f)
		});
		fgBox = new ComboBox
		{
			Location = new Point(260, y + 2),
			Width = 340,
			DropDownStyle = ComboBoxStyle.DropDownList,
			FlatStyle = FlatStyle.Flat,
			BackColor = Theme.Card,
			ForeColor = Theme.Text
		};
		fgBox.SelectedIndexChanged += (object s, EventArgs e) =>
		{
			PickFg();
		};
		page.Controls.Add(fgBox);
		y += 40;
		explain = new Label
		{
			Location = new Point(40, y),
			Size = new Size(900, 40),
			ForeColor = Theme.Muted,
			BackColor = Theme.Back,
			Font = Theme.Ui(9f),
			Text = L("Point at an option for the details. Tested: tried on edupode's PC (RTX 5070 Ti laptop).", "Passa o rato por uma opcao para ver os detalhes. Testado: experimentado no PC do edupode (portatil RTX 5070 Ti).", "Survole une option pour les détails. Testé : essayé sur le PC d'edupode (portable RTX 5070 Ti).")
		};
		page.Controls.Add(explain);
		ToolTip toolTip2 = new ToolTip();
		toolTip2.AutoPopDelay = 20000;
		toolTip2.InitialDelay = 300;
		ToolTip toolTip3 = toolTip2;
		foreach (KeyValuePair<CheckBox, string> item in Explanations())
		{
			CheckBox key = item.Key;
			string text3 = item.Value;
			toolTip3.SetToolTip(key, text3);
			key.MouseEnter += (object s, EventArgs e) =>
			{
				explain.Text = text3;
			};
		}
		y += 56;
		page.Controls.Add(new Rule
		{
			Location = new Point(40, y),
			Width = 900
		});
		y += 18;
		page.Controls.Add(new Heading(L("Activity", "Atividade", "Activité"))
		{
			Location = new Point(40, y + 3),
			Width = 300,
			Hair = false
		});
		Pill showLog = new Pill
		{
			Text = L("Show activity", "Mostrar atividade", "Afficher l'activité"),
			Location = new Point(750, y),
			Size = new Size(190, 32)
		};
		page.Controls.Add(showLog);
		y += 42;
		loadBar = new LoadBar
		{
			Location = new Point(40, y),
			Size = new Size(900, 26),
			Visible = false
		};
		page.Controls.Add(loadBar);
		y += 34;
		logBox = new TextBox
		{
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Vertical,
			ForeColor = Theme.Dim,
			Font = Theme.Mono(8.5f)
		};
		logPanel = FieldFor(logBox, 40, y, 900, 160, flat: true);
		Kit.Dark(logBox, "DarkMode_Explorer");
		logPanel.Visible = false;
		page.Controls.Add(logPanel);
		loadBarAnchor = showLog;
		showLog.Click += (object s, EventArgs e) =>
		{
			if (!logPanel.Visible)
			{
				logPanel.Top = showLog.Top + 76;
			}
			logPanel.Visible = !logPanel.Visible;
			showLog.Text = (logPanel.Visible ? L("Hide activity", "Esconder atividade", "Masquer l'activité") : L("Show activity", "Mostrar atividade", "Afficher l'activité"));
			showLog.Invalidate();
			if (logPanel.Visible)
			{
				page.ScrollControlIntoView(logPanel);
			}
		};
		page.Controls.Add(new Label
		{
			Location = new Point(40, y + 8),
			Size = new Size(10, 10),
			BackColor = Theme.Back
		});
		Fluid(page, 40, 900, null);
		return page;
	}

	private Dictionary<CheckBox, string> Explanations()
	{
		Dictionary<CheckBox, string> dictionary = new Dictionary<CheckBox, string>();
		dictionary[panelBox] = L("★ Shows FPS (now, last-minute average, whole-run average, worst), frame time, draws, video memory and RAM in the upper left corner. F3 hides it.", "★ Mostra os FPS (agora, media do ultimo minuto, media total, pior), tempo por frame, draws, VRAM e RAM no canto superior esquerdo. F3 esconde.");
		dictionary[borderlessBox] = L("★ A window without borders over the whole screen (test A: 22-24 FPS on edupode's PC). Needed for Smooth Motion and quick Alt+Tab.", "★ Janela sem bordas no ecra todo (teste A: 22-24 FPS no PC do edupode). Precisa-se para o Smooth Motion e para Alt+Tab rapido.");
		dictionary[fps60Box] = L("★ The game asks for 30 FPS; this lets its clock run up to 60, so cutscenes and menus go past 30 when the PC can.", "★ O jogo pede 30 FPS; isto deixa o relogio ir ate 60, por isso cutscenes e menus passam dos 30 quando o PC consegue.");
		dictionary[smoothBox] = L("New: NVIDIA's driver frame generation for RTX 40/50. The launcher writes it into the driver's profile for victory.exe, no NVIDIA App needed. Doubles the frames shown; adds a little input lag.", "Novo: a geracao de frames do driver NVIDIA para RTX 40/50. O launcher escreve-a no perfil do driver para o victory.exe, sem NVIDIA App. Duplica os frames mostrados; junta um pouco de atraso.");
		dictionary[reserveBox] = L("New: asks Windows to reserve video memory for the game, so other apps are moved out first when memory runs short. Fewer black and flat textures.", "Novo: pede ao Windows uma reserva de VRAM para o jogo, por isso as outras apps saem primeiro quando falta memoria. Menos texturas pretas e lisas.");
		dictionary[fightsBox] = L("Keeps 2 GB of the game's buffers on the card instead of 1.5: for crowds and fights, where the FPS drops. Needs a 12 GB card. Tested on edupode's PC: crowds 14-17 FPS instead of 10-15.", "Mantem 2 GB de buffers do jogo na placa em vez de 1,5: para multidoes e lutas, onde os FPS caem. Precisa de placa de 12 GB. Testado no PC do edupode: multidoes a 14-17 FPS em vez de 10-15.");
		dictionary[qualityBox] = L("Waits for every copy of the game's buffers before drawing: fewer stretched bodies ('spaghetti'), but several FPS slower.", "Espera por todas as copias dos buffers do jogo antes de desenhar: menos corpos esticados ('esparguete'), mas varios FPS mais lento.");
		dictionary[facesBox] = L("Waits for textures to finish uploading before use: fixes rainbow faces, costs FPS.", "Espera que as texturas acabem de subir antes de as usar: corrige as caras arco-iris, custa FPS.");
		dictionary[cpuBox] = L("★ Tested on edupode's PC: +1 FPS in the open world (21.6 vs 20.5) and 2.3 ms less preparing each frame. The CPU is the limit (the card waits ~0 ms a frame): 6 helper threads used to refresh the game's buffers and textures (~18 ms of a 44 ms frame); this gives them half your cores (up to 12) and asks Windows which pages changed on a thread of its own.", "★ Testado no PC do edupode: +1 FPS no mundo aberto (21,6 contra 20,5) e menos 2,3 ms a preparar cada frame. O limite e o processador (a placa espera ~0 ms por frame): 6 ajudantes refrescavam os buffers e as texturas do jogo (~18 ms de um frame de 44 ms); isto da-lhes metade dos teus nucleos (ate 12) e pergunta ao Windows que paginas mudaram numa thread a parte.");
		dictionary[warmBox] = L("New: before the game starts, a loading bar makes every pipeline earlier sessions drew with, in separate processes, so the driver has them ready: no stutter the first time you see a place. Takes seconds once the cache is ready; after a VIctory or driver update it takes longer, once.", "Novo: antes de o jogo arrancar, uma barra de carregamento prepara todas as pipelines usadas nas sessoes anteriores, em processos a parte, para o driver ja as ter: sem engasgos a primeira vez que ves um sitio. Demora segundos com a cache pronta; depois de atualizar o VIctory ou o driver demora mais, uma vez.");
		dictionary[loadingBox] = L("Experimental: textures the game builds on the CPU no longer wait for the game's memory to settle. Shorter loading pauses; untested.", "Experimental: as texturas que o jogo prepara no processador deixam de esperar que a memoria assente. Pausas de carregamento mais curtas; nao testado.");
		return dictionary;
	}

	private void Recommended()
	{
		SelectMode(0);
		panelBox.Checked = true;
		borderlessBox.Checked = true;
		fps60Box.Checked = true;
		reserveBox.Checked = true;
		smoothBox.Checked = rtxFrameGen;
		fightsBox.Checked = !integratedOnly && vramGb >= 11.0;
		cpuBox.Checked = true;
		qualityBox.Checked = false;
		facesBox.Checked = false;
		loadingBox.Checked = false;
		warmBox.Checked = true;
		Log(L("Recommended setup (= the TUDO + lutas shortcut): Recommended 720p, overlay, borderless, 60 FPS clock, 2 GB of buffers for crowds, video memory reserve" + (rtxFrameGen ? ", Smooth Motion." : "."), "Configuracao recomendada (= atalho TUDO + lutas): Recomendado 720p, overlay, sem bordas, relogio de 60 FPS, 2 GB de buffers para multidoes, reserva de VRAM" + (rtxFrameGen ? ", Smooth Motion." : "."), "Réglages recommandés (= raccourci TUDO + lutas) : Recommandé 720p, overlay, sans bordure, horloge à 60 FPS, 2 Go de buffers pour les foules, réserve de VRAM" + (rtxFrameGen ? ", Smooth Motion." : ".")));
		if (integratedOnly)
		{
			SelectMode(5);
			fps60Box.Checked = false;
			smoothBox.Checked = false;
			fightsBox.Checked = false;
		}
	}

	private void SelectMode(int k)
	{
		if (k < 0 || k >= modes.Length)
		{
			return;
		}
		modeIndex = k;
		foreach (ModeCard card in cards)
		{
			card.Selected = (int)card.Tag == k;
		}
		Refresh2();
	}

	private Panel MemoryPage()
	{
		Panel panel = Page();
		int num = PageTop(panel, L("Performance", "Desempenho", "Performances"), L("Memory", "Memoria", "Mémoire"), L("measured.", "medida.", "mesurée."), L("Video memory and RAM, and which apps are holding them.", "Memoria de video e RAM, e que apps as estao a ocupar.", "Mémoire vidéo et RAM, et quelles applis les occupent."));
		vramSummary = new Label
		{
			Location = new Point(40, num),
			Size = new Size(900, 46),
			ForeColor = Theme.Text,
			BackColor = Theme.Back,
			Font = new Font("Segoe UI Semibold", 10.5f)
		};
		panel.Controls.Add(vramSummary);
		num += 54;
		vramList = new ListView
		{
			Location = new Point(40, num),
			Size = new Size(900, 230),
			View = View.Details,
			CheckBoxes = true,
			FullRowSelect = true,
			BackColor = Theme.Card,
			ForeColor = Theme.Text,
			BorderStyle = BorderStyle.None,
			Font = new Font("Segoe UI", 10f),
			HeaderStyle = ColumnHeaderStyle.Nonclickable,
			OwnerDraw = true
		};
		vramList.Columns.Add(L("App", "App"), 230);
		vramList.Columns.Add(L("Video memory", "VRAM"), 130, HorizontalAlignment.Right);
		vramList.Columns.Add("RAM", 110, HorizontalAlignment.Right);
		vramList.Columns.Add(L("Note", "Nota"), 400);
		DarkHeader(vramList);
		vramList.Resize += (object s, EventArgs e) =>
		{
			int num2 = vramList.ClientSize.Width - vramList.Columns[0].Width - vramList.Columns[1].Width - vramList.Columns[2].Width;
			if (num2 > 120)
			{
				vramList.Columns[3].Width = num2;
			}
		};
		vramEmpty = new Label
		{
			Text = L("No app is holding much video memory or RAM.", "Nenhuma app ocupa muita VRAM ou RAM.", "Aucune appli n'occupe beaucoup de VRAM ou de RAM."),
			TextAlign = ContentAlignment.MiddleCenter,
			ForeColor = Theme.Dim,
			BackColor = Theme.Card,
			Location = new Point(0, 40),
			Size = new Size(900, 60),
			Visible = false
		};
		vramList.Controls.Add(vramEmpty);
		vramList.Resize += (object s, EventArgs e) =>
		{
			vramEmpty.Width = vramList.ClientSize.Width;
		};
		vramList.ItemCheck += (object s, ItemCheckEventArgs e) =>
		{
			if (vramList.Items[e.Index].Tag is MemoryUser { Protected: not false })
			{
				e.NewValue = CheckState.Unchecked;
			}
		};
		panel.Controls.Add(vramList);
		num += 244;
		Pill pill = new Pill();
		pill.Text = L("Refresh", "Atualizar", "Actualiser");
		pill.Location = new Point(40, num);
		pill.Size = new Size(140, 34);
		Pill pill2 = pill;
		pill2.Click += (object s, EventArgs e) =>
		{
			ScanMemory();
		};
		panel.Controls.Add(pill2);
		Pill pill3 = new Pill();
		pill3.Text = L("Close the checked apps", "Fechar as apps marcadas");
		pill3.Location = new Point(190, num);
		pill3.Size = new Size(230, 34);
		Pill pill4 = pill3;
		pill4.Click += (object s, EventArgs e) =>
		{
			CloseChecked();
		};
		panel.Controls.Add(pill4);
		num += 58;
		panel.Controls.Add(new Heading("NVIDIA Smooth Motion")
		{
			Location = new Point(40, num),
			Width = 900
		});
		num += 34;
		smoothStatus = new Label
		{
			Location = new Point(40, num),
			Size = new Size(900, 58),
			ForeColor = Theme.Text,
			BackColor = Theme.Back
		};
		panel.Controls.Add(smoothStatus);
		num += 62;
		Pill pill5 = new Pill();
		pill5.Text = L("Turn it on now", "Ligar agora");
		pill5.Location = new Point(40, num);
		pill5.Size = new Size(170, 34);
		Pill pill6 = pill5;
		pill6.Click += (object s, EventArgs e) =>
		{
			ApplySmooth(on: true, loud: true);
		};
		panel.Controls.Add(pill6);
		Pill pill7 = new Pill();
		pill7.Text = L("Turn it off", "Desligar");
		pill7.Location = new Point(220, num);
		pill7.Size = new Size(150, 34);
		Pill pill8 = pill7;
		pill8.Click += (object s, EventArgs e) =>
		{
			ApplySmooth(on: false, loud: true);
		};
		panel.Controls.Add(pill8);
		num += 58;
		panel.Controls.Add(new Heading(L("Getting the most video memory", "Ter o maximo de VRAM"))
		{
			Location = new Point(40, num),
			Width = 900
		});
		num += 34;
		Label tips = new Label
		{
			Location = new Point(40, num),
			Size = new Size(900, 250),
			ForeColor = Theme.Dim,
			BackColor = Theme.Back
		};
		Label label = tips;
		string[] value;
		if (Pt)
		{
			value = new string[8] { "Mais VRAM livre = mais texturas na placa = menos coisas pretas, texturas lisas e recarregamentos.", "•  NVIDIA App > Definicoes > Funcionalidades: desliga o Instant Replay e o overlay (o 'NVIDIA Overlay' pode ter 1-2 GB).", "•  Discord: Definicoes > Avancado > Aceleracao de hardware: desligada. Wallpaper Engine: pausa ou fecha.", "•  Browsers, emuladores e outros jogos: fecha-os enquanto jogas.", "•  Portateis com MUX (PredatorSense, Armoury Crate, Legion Vantage): no modo Hibrido/Optimus o ambiente de trabalho", "   corre na grafica integrada e liberta normalmente 0,5-1,5 GB na NVIDIA.", "•  'Guardar o maximo de VRAM' pede ao Windows uma reserva para o VIctory: quando outra app pede memoria, a dela sai primeiro.", "•  O Smooth Motion e escrito no perfil do driver para victory.exe (como o NVIDIA Profile Inspector): nao precisa da NVIDIA App." };
		}
		else
		{
			value = (Fr ? new string[8] { "Plus de VRAM libre = plus de textures sur la carte = moins d'objets noirs, de textures plates et de rechargements.", "•  NVIDIA App > Paramètres > Fonctionnalités : désactive Instant Replay et l'overlay ('NVIDIA Overlay' peut prendre 1-2 Go).", "•  Discord : Paramètres > Avancés > Accélération matérielle : désactivée. Wallpaper Engine : mets-le en pause ou quitte-le.", "•  Navigateurs, émulateurs et autres jeux : ferme-les pendant que tu joues.", "•  Portables avec MUX (PredatorSense, Armoury Crate, Legion Vantage) : en mode Hybride/Optimus le bureau", "   tourne sur la carte intégrée, ce qui libère en général 0,5-1,5 Go sur la carte NVIDIA.", "•  'Garder le max de VRAM' demande à Windows une réserve pour VIctory : les autres applis sortent en premier.", "•  Smooth Motion est écrit dans le profil du pilote pour victory.exe (comme NVIDIA Profile Inspector) : pas besoin de NVIDIA App." } : new string[8] { "More free video memory = more textures on the card = fewer black objects, flat textures and reloads.", "•  NVIDIA App > Settings > Features: turn off Instant Replay and the overlay ('NVIDIA Overlay' can hold 1-2 GB).", "•  Discord: Settings > Advanced > Hardware acceleration: off. Wallpaper Engine: pause or quit it.", "•  Browsers, emulators and other games: close them while you play.", "•  Laptops with a MUX switch (PredatorSense, Armoury Crate, Legion Vantage): in Hybrid/Optimus mode the desktop", "   runs on the integrated GPU, which usually frees 0.5-1.5 GB on the NVIDIA card.", "•  'Keep the most video memory' asks Windows to reserve memory for VIctory: other apps are moved out first.", "•  Smooth Motion is written into the driver's profile for victory.exe (as NVIDIA Profile Inspector does): no NVIDIA App needed." });
		}
		label.Text = string.Join("\r\n", value);
		panel.Controls.Add(tips);
		Label tipsEnd = new Label
		{
			Location = new Point(40, num + 260),
			Size = new Size(10, 10),
			BackColor = Theme.Back
		};
		panel.Controls.Add(tipsEnd);
		Fluid(panel, 40, 900, (int w) =>
		{
			int num2 = TextRenderer.MeasureText(tips.Text, tips.Font, new Size(w, 0), TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak).Height;
			tips.Height = num2 + 6;
			tipsEnd.Top = tips.Bottom + 4;
		});
		return panel;
	}

	private static void DarkHeader(ListView list)
	{
		list.DrawColumnHeader += (object s, DrawListViewColumnHeaderEventArgs e) =>
		{
			using (SolidBrush brush = new SolidBrush(Theme.Side))
			{
				e.Graphics.FillRectangle(brush, e.Bounds);
			}
			using SolidBrush brush2 = new SolidBrush(Theme.Dim);
			using Font font = new Font("Segoe UI Semibold", 9.5f);
			StringFormat format = new StringFormat
			{
				LineAlignment = StringAlignment.Center,
				Alignment = ((e.Header.TextAlign == HorizontalAlignment.Right) ? StringAlignment.Far : StringAlignment.Near)
			};
			Rectangle rectangle = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
			e.Graphics.DrawString(e.Header.Text, font, brush2, rectangle, format);
		};
		list.DrawItem += (object s, DrawListViewItemEventArgs e) =>
		{
			e.DrawDefault = true;
		};
		list.DrawSubItem += (object s, DrawListViewSubItemEventArgs e) =>
		{
			e.DrawDefault = true;
		};
	}

	private static List<MemoryUser> GpuUsers(out long cardUsed)
	{
		cardUsed = 0L;
		Dictionary<string, Dictionary<int, long>> dictionary = new Dictionary<string, Dictionary<int, long>>();
		try
		{
			InstanceDataCollection instanceDataCollection = new PerformanceCounterCategory("GPU Process Memory").ReadCategory()["Dedicated Usage"];
			foreach (InstanceData value5 in instanceDataCollection.Values)
			{
				string[] array = value5.InstanceName.Split('_');
				if (array.Length >= 5 && !(array[0] != "pid") && int.TryParse(array[1], out var result))
				{
					string key = array[3] + "_" + array[4];
					if (!dictionary.TryGetValue(key, out var value))
					{
						value = (dictionary[key] = new Dictionary<int, long>());
					}
					value.TryGetValue(result, out var value2);
					value[result] = value2 + value5.RawValue;
				}
			}
			InstanceDataCollection instanceDataCollection2 = new PerformanceCounterCategory("GPU Adapter Memory").ReadCategory()["Dedicated Usage"];
			string text = null;
			foreach (InstanceData value6 in instanceDataCollection2.Values)
			{
				if (value6.RawValue > cardUsed)
				{
					cardUsed = value6.RawValue;
					text = value6.InstanceName;
				}
			}
			if (text == null)
			{
				return new List<MemoryUser>();
			}
			string[] array2 = text.Split('_');
			string key2 = ((array2.Length >= 3) ? (array2[1] + "_" + array2[2]) : "");
			if (!dictionary.TryGetValue(key2, out var value3))
			{
				return new List<MemoryUser>();
			}
			Dictionary<string, MemoryUser> dictionary3 = new Dictionary<string, MemoryUser>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<int, long> item in value3)
			{
				string processName;
				try
				{
					processName = Process.GetProcessById(item.Key).ProcessName;
				}
				catch (Exception)
				{
					continue;
				}
				if (!dictionary3.TryGetValue(processName, out var value4))
				{
					value4 = (dictionary3[processName] = new MemoryUser
					{
						Name = processName
					});
				}
				value4.Vram += item.Value;
			}
			return dictionary3.Values.ToList();
		}
		catch (Exception)
		{
			return new List<MemoryUser>();
		}
	}

	private void ScanMemory()
	{
		vramSummary.Text = L("Looking...", "A ver...");
		Thread thread = new Thread(() =>
		{
			List<MemoryUser> source = GpuUsers(out var cardUsed);
			Dictionary<string, MemoryUser> dictionary = source.ToDictionary((MemoryUser u) => u.Name, StringComparer.OrdinalIgnoreCase);
			Process[] processes = Process.GetProcesses();
			foreach (Process process in processes)
			{
				try
				{
					if (!dictionary.TryGetValue(process.ProcessName, out var value))
					{
						value = (dictionary[process.ProcessName] = new MemoryUser
						{
							Name = process.ProcessName
						});
					}
					value.Ram += process.PrivateMemorySize64;
				}
				catch (Exception)
				{
				}
			}
			long victory = 0L;
			if (dictionary.TryGetValue("victory", out var value2))
			{
				victory = value2.Vram;
			}
			List<MemoryUser> shown = (from u in dictionary.Values
				where u.Vram >= 41943040 || u.Ram >= 419430400
				orderby u.Vram * 4 + u.Ram descending
				select u).ToList();
			foreach (MemoryUser item in shown)
			{
				item.Protected = ProtectedNames.Contains(item.Name);
				if (item.Name.Equals("dwm", StringComparison.OrdinalIgnoreCase))
				{
					item.Note = L("Windows desktop: see the MUX tip below", "Ambiente de trabalho: ve a dica do MUX em baixo");
				}
				else if (item.Name.Equals("victory", StringComparison.OrdinalIgnoreCase))
				{
					item.Note = L("the game", "o jogo");
				}
				else if (item.Name.StartsWith("NVIDIA Overlay", StringComparison.OrdinalIgnoreCase))
				{
					item.Note = L("turn off Instant Replay / overlay in NVIDIA App", "desliga Instant Replay / overlay na NVIDIA App");
				}
				else if (item.Name.Equals("Discord", StringComparison.OrdinalIgnoreCase))
				{
					item.Note = L("or turn off its hardware acceleration", "ou desliga a aceleracao de hardware");
				}
				else if (item.Name.StartsWith("wallpaper", StringComparison.OrdinalIgnoreCase))
				{
					item.Note = "Wallpaper Engine";
				}
				else if (item.Name.Equals("msedgewebview2", StringComparison.OrdinalIgnoreCase))
				{
					item.Note = L("part of other apps (protected)", "parte de outras apps (protegido)");
				}
				else if (item.Protected)
				{
					item.Note = L("system or needed (protected)", "sistema ou necessario (protegido)");
				}
			}
			ComputerInfo computerInfo = new ComputerInfo();
			double ramTotal = (double)computerInfo.TotalPhysicalMemory / 1073741824.0;
			double ramFree = (double)computerInfo.AvailablePhysicalMemory / 1073741824.0;
			BeginInvoke((Action)(() =>
			{
				double num2 = 1073741824.0;
				long num3 = Math.Max(0L, cardUsed - victory);
				vramSummary.Text = ((cardUsed == 0) ? L("Could not read the video memory counters (they need Windows 10 1709 or later).", "Nao deu para ler os contadores de VRAM (precisam do Windows 10 1709 ou mais recente).") : string.Format(L("Card: {0:0.0} of {1:0.0} GB in use  —  VIctory {2:0.0} GB, everything else {3:0.0} GB\r\nRAM: {4:0.0} of {5:0.0} GB free", "Placa: {0:0.0} de {1:0.0} GB em uso  —  VIctory {2:0.0} GB, o resto {3:0.0} GB\r\nRAM: {4:0.0} de {5:0.0} GB livres"), (double)cardUsed / num2, vramGb, (double)victory / num2, (double)num3 / num2, ramFree, ramTotal));
				vramSummary.ForeColor = ((num3 > 2147483648u) ? Theme.Warn : Theme.Text);
				vramList.BeginUpdate();
				vramList.Items.Clear();
				foreach (MemoryUser item2 in shown)
				{
					ListViewItem listViewItem = new ListViewItem(new string[4]
					{
						item2.Name,
						(item2.Vram > 0) ? ((item2.Vram >> 20) + " MB") : "-",
						(item2.Ram >> 20) + " MB",
						item2.Note
					})
					{
						Tag = item2
					};
					if (item2.Protected)
					{
						listViewItem.ForeColor = Theme.Dim;
					}
					vramList.Items.Add(listViewItem);
				}
				vramList.EndUpdate();
				vramEmpty.Visible = vramList.Items.Count == 0;
			}));
		});
		thread.IsBackground = true;
		thread.Start();
	}

	private void CloseChecked()
	{
		List<string> list = (from ListViewItem i in vramList.CheckedItems
			select i.Tag as MemoryUser into u
			where u != null && !u.Protected
			select u.Name).ToList();
		if (list.Count == 0)
		{
			MessageBox.Show(this, L("Check the apps to close first.", "Marca primeiro as apps a fechar."));
			return;
		}
		string text = L("Close these apps? Unsaved work in them is lost.\r\n\r\n", "Fechar estas apps? O que nao estiver guardado nelas perde-se.\r\n\r\n") + string.Join("\r\n", list);
		if (MessageBox.Show(this, text, "Julia's Launcher", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
		{
			return;
		}
		foreach (string item in list)
		{
			Process[] processesByName = Process.GetProcessesByName(item);
			foreach (Process process in processesByName)
			{
				try
				{
					if (!process.CloseMainWindow() || !process.WaitForExit(1500))
					{
						process.Kill();
					}
				}
				catch (Exception)
				{
				}
			}
		}
		Thread.Sleep(800);
		ScanMemory();
	}

	private void UpdateSmooth()
	{
		if (smoothStatus != null)
		{
			string text;
			if (smoothState == "on")
			{
				text = L("ON for victory.exe", "LIGADO para o victory.exe");
			}
			else if (smoothState == "off")
			{
				text = L("off for victory.exe", "desligado para o victory.exe");
			}
			else
			{
				text = ((smoothState == "not set") ? L("not set yet (PLAY sets it when the option is on)", "ainda nao configurado (o JOGAR configura com a opcao ligada)") : smoothState);
			}
			smoothStatus.Text = L("Driver frame generation: the card shows a made-up frame between two of the game's. Now: ", "Geracao de frames do driver: a placa mostra um frame inventado entre dois do jogo. Agora: ") + text + (rtxFrameGen ? "" : L("\r\nNeeds an RTX 40 or 50 series card and driver 571.86 or newer.", "\r\nPrecisa de uma placa RTX 40 ou 50 e do driver 571.86 ou mais recente."));
			smoothStatus.ForeColor = ((smoothState == "on") ? Theme.Good : Theme.Text);
		}
	}

	private void ApplySmooth(bool on, bool loud)
	{
		Thread thread = new Thread(() =>
		{
			string err = SmoothMotion.Set(on);
			string s = SmoothMotion.Status();
			BeginInvoke((Action)(() =>
			{
				smoothState = s;
				UpdateSmooth();
				UpdateBanner();
				if (err != null)
				{
					Log("Smooth Motion: " + err);
				}
				else
				{
					Log(on ? L("Smooth Motion is on for victory.exe (driver profile).", "Smooth Motion ligado para o victory.exe (perfil do driver).") : L("Smooth Motion is off for victory.exe.", "Smooth Motion desligado para o victory.exe."));
				}
				if (loud && err != null)
				{
					MessageBox.Show(this, "Smooth Motion: " + err, "Julia's Launcher");
				}
			}));
		});
		thread.IsBackground = true;
		thread.Start();
	}

	private Panel ControlsPage()
	{
		Panel panel = Page();
		int num = PageTop(panel, L("Controls", "Controlos", "Commandes"), L("Controls", "Controlos", "Commandes"), L("by what you do.", "pelo que fazes.", "selon l'action."), null);
		panel.Controls.Add(Small(L("Keyboard and mouse, and the Xbox controller button for each. A controller connected to the PC replaces all of this.", "Teclado e rato, e o botao do comando Xbox de cada um. Um comando ligado ao PC substitui tudo isto.", "Clavier et souris, et le bouton de la manette Xbox pour chacun. Une manette branchée au PC remplace tout cela."), 40, num, 900));
		panel.Controls[panel.Controls.Count - 1].Height = 38;
		num += 40;
		Func<string, string, string, string[]> func = (string what, string key, string pad) => new string[3] { what, key, pad };
		List<Tuple<string, string[][]>> list = new List<Tuple<string, string[][]>>();
		list.Add(Tuple.Create(L("On foot", "A pe", "À pied"), new string[7][]
		{
			func(L("Move", "Andar", "Se déplacer"), "W A S D", L("Left stick", "Analogico esq.", "Stick gauche")),
			func(L("Look around (click the game to capture the mouse)", "Olhar (clica no jogo para capturar o rato)", "Regarder (clique dans le jeu pour capturer la souris)"), L("Mouse", "Rato", "Souris"), L("Right stick", "Analogico dir.", "Stick droit")),
			func(L("Run", "Correr", "Courir"), L("Left Shift", "Shift esq.", "Maj gauche"), "L3"),
			func(L("Jump, climb", "Saltar, escalar", "Sauter, grimper"), L("Space / Enter", "Espaco / Enter", "Espace / Entrée"), "A"),
			func(L("Crouch, rest", "Agachar, descansar", "S'accroupir, se reposer"), L("Left Ctrl / C / Backspace", "Ctrl esq. / C / Backspace", "Ctrl gauche / C / Retour"), "B"),
			func(L("Take cover", "Proteger-se", "Se mettre à couvert"), "Q", "RB"),
			func(L("Look back", "Olhar para tras", "Regarder derrière"), "V", "R3")
		}));
		list.Add(Tuple.Create(L("Talking and using things", "Falar e usar coisas", "Parler et utiliser"), new string[2][]
		{
			func(L("Interact, talk", "Interagir, falar", "Interagir, parler"), "E / Y", "Y"),
			func(L("Aim, interact", "Mirar, interagir", "Viser, interagir"), L("Right mouse button", "Botao direito", "Clic droit"), "LT")
		}));
		list.Add(Tuple.Create(L("Fighting", "Lutar", "Combat"), new string[4][]
		{
			func(L("Shoot", "Disparar", "Tirer"), L("Left mouse button", "Botao esquerdo", "Clic gauche"), "RT / RB"),
			func(L("Punch, kick", "Soco, pontape", "Coup de poing, de pied"), "R / X", "X"),
			func(L("Block", "Bloquear", "Bloquer"), L("Space", "Espaco", "Espace"), "A"),
			func(L("Inventory, weapon wheel (hold)", "Inventario, roda de armas (manter)", "Inventaire, roue des armes (maintenir)"), "Tab", "LB")
		}));
		list.Add(Tuple.Create(L("Driving (press F: a high beep means vehicle mode)", "Conduzir (carrega no F: um apito agudo e o modo veiculo)", "Conduire (appuie sur F : un bip aigu = mode véhicule)"), new string[9][]
		{
			func(L("Get in, get out", "Entrar, sair", "Monter, descendre"), "F", L("Y + vehicle mode", "Y + modo veiculo", "Y + mode véhicule")),
			func(L("Accelerate", "Acelerar", "Accélérer"), "W", "RT"),
			func(L("Brake, reverse", "Travar, marcha-atras", "Freiner, reculer"), "S", "LT"),
			func(L("Steer", "Virar", "Tourner"), "A / D", L("Left stick", "Analogico esq.", "Stick gauche")),
			func(L("Handbrake", "Travao de mao", "Frein à main"), L("Space", "Espaco", "Espace"), "A"),
			func(L("Horn, nitro", "Buzina, nitro", "Klaxon, nitro"), L("Left Shift", "Shift esq.", "Maj gauche"), "L3"),
			func(L("Headlights", "Farois", "Phares"), L("Left Ctrl / C", "Ctrl esq. / C", "Ctrl gauche / C"), "B"),
			func(L("Draw a weapon", "Sacar arma", "Sortir une arme"), "Tab", "LB"),
			func(L("Vehicle mode out of sync? Set it right", "Modo veiculo trocado? Acerta-o", "Mode véhicule décalé ? Le recaler"), "Caps Lock", "-")
		}));
		list.Add(Tuple.Create(L("Flying", "Voar", "Voler"), new string[4][]
		{
			func(L("Take off", "Levantar voo", "Décoller"), "W + K", "-"),
			func(L("Nose down, nose up", "Nariz baixo, nariz cima", "Piquer, cabrer"), "I / K", L("Left stick up/down", "Analogico esq. cima/baixo", "Stick gauche haut/bas")),
			func(L("Turn left", "Virar a esquerda", "Tourner à gauche"), "R / X", "X"),
			func(L("Turn right", "Virar a direita", "Tourner à droite"), L("Left Ctrl / C", "Ctrl esq. / C", "Ctrl gauche / C"), "B")
		}));
		list.Add(Tuple.Create(L("Map, phone and menus", "Mapa, telemovel e menus", "Carte, téléphone et menus"), new string[3][]
		{
			func(L("Map", "Mapa", "Carte"), "M", "View"),
			func(L("Phone, radio, partner", "Telemovel, radio, parceiro", "Téléphone, radio, partenaire"), L("Arrows / 1-4", "Setas / 1-4", "Flèches / 1-4"), L("D-pad", "D-pad", "Croix")),
			func(L("Pause", "Pausa", "Pause"), "Esc", "Menu")
		}));
		list.Add(Tuple.Create(L("VIctory's own keys", "Teclas do VIctory", "Touches de VIctory"), new string[3][]
		{
			func(L("Release the mouse", "Soltar o rato", "Libérer la souris"), "Alt / F1", "-"),
			func(L("Show or hide the overlay (FPS, memory)", "Mostrar ou esconder o overlay (FPS, memoria)", "Afficher ou masquer l'overlay (FPS, mémoire)"), "F3", "-"),
			func(L("Record a frame (recording mode)", "Gravar um frame (modo de gravacao)", "Enregistrer une image (mode enregistrement)"), "F9", "-")
		}));
		List<Tuple<string, string[][]>> list2 = list;
		foreach (Tuple<string, string[][]> item2 in list2)
		{
			panel.Controls.Add(new Heading(item2.Item1)
			{
				Location = new Point(40, num),
				Width = 900
			});
			num += 32;
			string[][] item = item2.Item2;
			foreach (string[] array in item)
			{
				panel.Controls.Add(new Label
				{
					Text = array[0],
					Location = new Point(48, num + 4),
					Size = new Size(500, 22),
					ForeColor = Theme.Text,
					BackColor = Theme.Back,
					Font = Theme.Ui(9.75f),
					AutoEllipsis = true
				});
				panel.Controls.Add(new KeyCap
				{
					Text = array[1],
					Location = new Point(560, num),
					Size = new Size(220, 28)
				});
				panel.Controls.Add(new KeyCap
				{
					Text = array[2],
					Pad = true,
					Location = new Point(792, num),
					Size = new Size(148, 28)
				});
				num += 34;
			}
			num += 12;
		}
		panel.Controls.Add(new Label
		{
			Location = new Point(40, num),
			Size = new Size(10, 10),
			BackColor = Theme.Back
		});
		Fluid(panel, 40, 900, null);
		return panel;
	}

	private Panel HelpPage()
	{
		Panel panel = Page();
		int num = PageTop(panel, L("Help", "Ajuda", "Aide"), L("Help", "Ajuda", "Aide"), L("when you need it.", "quando precisares.", "quand il faut."), L("Everything the launcher does, and how to reach us when something breaks.", "Tudo o que o launcher faz, e como falar connosco quando algo avaria.", "Tout ce que fait le launcher, et comment nous joindre quand quelque chose casse."));
		Pill pill = new Pill();
		pill.Main = true;
		pill.Text = L("Send errors (GitHub / Discord)", "Enviar erros (GitHub / Discord)");
		pill.Location = new Point(40, num);
		pill.Size = new Size(240, 38);
		Pill pill2 = pill;
		pill2.Click += (object s, EventArgs e) =>
		{
			ReportErrors();
		};
		panel.Controls.Add(pill2);
		Pill pill3 = new Pill();
		pill3.Text = L("Join the Discord", "Entrar no Discord");
		pill3.Location = new Point(290, num);
		pill3.Size = new Size(170, 38);
		Pill pill4 = pill3;
		pill4.Click += (object s, EventArgs e) =>
		{
			OpenUrl("https://discord.gg/648WEXCAsZ");
		};
		panel.Controls.Add(pill4);
		Pill pill5 = new Pill();
		pill5.Text = L("Open the logs folder", "Abrir a pasta dos logs");
		pill5.Location = new Point(470, num);
		pill5.Size = new Size(200, 38);
		Pill pill6 = pill5;
		pill6.Click += (object s, EventArgs e) =>
		{
			Directory.CreateDirectory(Path.Combine(dataRoot, "logs"));
			OpenUrl(Path.Combine(dataRoot, "logs"));
		};
		panel.Controls.Add(pill6);
		Pill pill7 = new Pill();
		pill7.Text = L("What's new", "Novidades", "Nouveautés");
		pill7.Location = new Point(680, num);
		pill7.Size = new Size(120, 38);
		Pill pill8 = pill7;
		pill8.Click += (object s, EventArgs e) =>
		{
			ShowWhatsNew();
		};
		panel.Controls.Add(pill8);
		Pill pill9 = new Pill();
		pill9.Text = L("Is it safe?", "E seguro?", "Est-ce sûr ?");
		pill9.Location = new Point(810, num);
		pill9.Size = new Size(130, 38);
		Pill pill10 = pill9;
		pill10.Click += (object s, EventArgs e) =>
		{
			OpenUrl("https://github.com/felicienradio/cold-project-vi/blob/main/docs/SAFETY.md");
		};
		panel.Controls.Add(pill10);
		num += 54;
		TextBox text = new TextBox
		{
			Location = new Point(40, num),
			Size = new Size(900, 1180),
			Multiline = true,
			ReadOnly = true,
			BorderStyle = BorderStyle.None,
			ForeColor = Theme.Text,
			BackColor = Theme.Back,
			Font = Theme.Ui(10f),
			ScrollBars = ScrollBars.None,
			TabStop = false
		};
		text.Text = string.Join("\r\n", FunctionList().Concat(new string[3]
		{
			"",
			L("ABOUT", "SOBRE"),
			L("Julia's Launcher is edupode's remake, built on the work of:", "O Julia's Launcher e o remake do edupode, feito com o trabalho de:")
		}).Concat(Credits()).ToArray());
		panel.Controls.Add(text);
		Label helpEnd = new Label
		{
			Location = new Point(40, num + 1190),
			Size = new Size(10, 10),
			BackColor = Theme.Back
		};
		panel.Controls.Add(helpEnd);
		Fluid(panel, 40, 900, (int w) =>
		{
			int num2 = TextRenderer.MeasureText(text.Text, text.Font, new Size(w - 8, 0), TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak).Height;
			text.Height = num2 + 16;
			helpEnd.Top = text.Bottom + 4;
		});
		return panel;
	}

	private IEnumerable<string> FunctionList()
	{
		if (!Pt)
		{
			if (!Fr)
			{
				return new string[48]
				{
					"PLAY",
					"   •  Presets: Recommended (720p), Fastest and Modest PC (540p), Ultra Low (PCs with very little memory), Sharp and",
					"      Classic cgbj (1080p), Custom (your values). The launcher picks one from your hardware the first time.",
					"   •  Launch options: point at each to see what it does. ★ marks what was tested.",
					"   •  PLAY loads the pipelines, starts the game and, when it closes, keeps this session's pipelines for the next one.",
					"   •  A stuck game (an error, a window that stopped answering) never blocks PLAY: it turns into 'CLOSE AND PLAY'.",
					"   •  When something is missing (virtual memory, power, Resizable BAR, shaders), a yellow notice says what and takes you to fix it.",
					"",
					"SETUP",
					"   •  'Do everything for me' runs the missing steps: engine, game folder, shaders, power, virtual",
					"      memory, the right graphics card for VIctory and frame generation. Windows asks for administrator rights when needed.",
					"   •  Resizable BAR: the launcher checks what D3D12 gives the game (not only the BIOS) and shows your motherboard's guide.",
					"",
					"SHADERS",
					"   •  Shows, live, the translated shaders, the pipelines ready, the ones that failed, disk space and errors.",
					"   •  'Repair' makes what is missing; 'Rebuild everything' clears the cache and prepares again.",
					"",
					"PERFORMANCE",
					"   •  What holds video memory and RAM; check apps and close them before playing. Frame generation on or off.",
					"",
					"HARDWARE & ID",
					"   •  Your permanent ID (VI-XXXX-XXXX), with a copy button. It comes from PC parts and stays after a Windows reinstall.",
					"   •  Hardware sharing (off by default): sends ONLY hardware (graphics card, CPU, RAM, motherboard, Windows, disk type).",
					"      It never sends logs (those have a choice of their own), options, paths, names or your IP. The page shows exactly what is sent.",
					"",
					"UPDATES",
					"   •  The launcher checks GitHub when it opens and every 5 minutes. An update is installed only when its signature and",
					"      SHA-256 match; it installs itself while the game is not running. It is the only thing the launcher takes from the internet.",
					"",
					"ADVANCED TWEAKS (CUSTOM)",
					"   •  Resolution, textures, buffers, memory guards and FPS clock. 'Copy my profile' gives a code (VI1-...) to share;",
					"      paste a friend's in the box and press 'Apply' (it shows what changes first).",
					"",
					"LOGS AND ERRORS",
					"   •  Game logs: " + Path.Combine(dataRoot, "logs") + " (session-*.log, prepare.log).",
					"   •  Launcher errors: " + Program.ErrorFile,
					"   •  'Send errors to Discord': choose 'launcher errors' or 'game errors' and write what happened. The launcher puts the",
					"      right files in a zip on your Desktop, opens the folder and asks you to send it in the \"Send bugs here please\" channel.",
					"      Nothing is sent on its own: you send it.",
					"",
					"GRAPHICS CARDS",
					"   •  NVIDIA RTX 40/50: Smooth Motion. RTX 30: NVSmooth30 (experimental). Other cards (GTX 10, RTX 20, AMD, Intel): VI frame generation",
					"      (experimental, off by default): a frame between each two of the game's, made from the pictures alone, half a frame later.",
					"   •  Every card (NVIDIA, AMD, Intel) plays with the edupode engine; Radeon cards can pick Belobug's alternative.",
					"",
					"KEYS IN THE GAME",
					"   •  F3 shows/hides the overlay: FPS, last minute's average (AVG 1M), average since the start (ALL), worst half second (LOW).",
					"   •  Alt or F1 releases the mouse. The Controls page has the rest."
				};
			}
			return new string[48]
			{
				"JOUER",
				"   •  Préréglages : Recommandé (720p), Le plus rapide et PC modeste (540p), Ultra bas (PC avec très peu de mémoire), Net et",
				"      cgbj classique (1080p), Personnalisé (tes valeurs). Le launcher en choisit un selon ton matériel la première fois.",
				"   •  Options de lancement : survole chacune pour voir ce qu'elle fait. ★ marque ce qui a été testé.",
				"   •  JOUER charge les pipelines, lance le jeu et, à sa fermeture, garde les pipelines de cette session pour la prochaine.",
				"   •  Un jeu bloqué (une erreur, une fenêtre qui ne répond plus) ne bloque jamais JOUER : il devient 'FERMER ET JOUER'.",
				"   •  Quand il manque quelque chose (mémoire virtuelle, alimentation, Resizable BAR, shaders), un avis jaune dit quoi et t'emmène corriger.",
				"",
				"INSTALLATION",
				"   •  'Tout faire pour moi' lance les étapes manquantes : moteur, dossier du jeu, shaders, alimentation,",
				"      mémoire virtuelle, bonne carte graphique pour VIctory et génération d'images. Windows demande les droits administrateur si besoin.",
				"   •  Resizable BAR : le launcher vérifie ce que D3D12 donne au jeu (pas seulement le BIOS) et montre le guide de ta carte mère.",
				"",
				"SHADERS",
				"   •  Affiche en direct les shaders traduits, les pipelines prêtes, celles en échec, l'espace disque et les erreurs.",
				"   •  'Réparer' fait ce qui manque ; 'Tout refaire' vide le cache et prépare à nouveau.",
				"",
				"PERFORMANCES",
				"   •  Ce qui occupe la VRAM et la RAM ; coche des applis et ferme-les avant de jouer. Génération d'images activée ou non.",
				"",
				"MATÉRIEL ET ID",
				"   •  Ton ID permanent (VI-XXXX-XXXX), avec un bouton copier. Il vient des composants du PC et reste après une réinstallation de Windows.",
				"   •  Partage du matériel (désactivé par défaut) : envoie UNIQUEMENT le matériel (carte graphique, processeur, RAM, carte mère, Windows,",
				"      type de disque). Jamais de logs (ceux-ci ont leur propre choix), d'options, de chemins, de noms ni ton IP. La page montre exactement ce qui est envoyé.",
				"",
				"MISES À JOUR",
				"   •  Le launcher vérifie GitHub à l'ouverture et toutes les 5 minutes. Une mise à jour n'est installée que si sa signature et",
				"      son SHA-256 correspondent ; elle s'installe seule quand le jeu ne tourne pas. C'est la seule chose que le launcher reçoit d'internet.",
				"",
				"RÉGLAGES AVANCÉS (PERSONNALISÉ)",
				"   •  Résolution, textures, buffers, protections mémoire et horloge FPS. 'Copier mon profil' donne un code (VI1-...) à partager ;",
				"      colle celui d'un ami dans la case et appuie sur 'Appliquer' (il montre d'abord ce qui change).",
				"",
				"LOGS ET ERREURS",
				"   •  Logs du jeu : " + Path.Combine(dataRoot, "logs") + " (session-*.log, prepare.log).",
				"   •  Erreurs du launcher : " + Program.ErrorFile,
				"   •  'Envoyer les erreurs sur Discord' : choisis 'erreurs du launcher' ou 'erreurs du jeu' et écris ce qui s'est passé. Le launcher met",
				"      les bons fichiers dans un zip sur ton Bureau, ouvre le dossier et te demande de l'envoyer dans le salon \"Send bugs here please\".",
				"      Rien n'est envoyé tout seul : c'est toi qui l'envoies.",
				"",
				"CARTES GRAPHIQUES",
				"   •  NVIDIA RTX 40/50 : Smooth Motion. RTX 30 : NVSmooth30 (expérimental). Autres cartes (GTX 10, RTX 20, AMD, Intel) : génération d'images VI",
				"      (expérimental, désactivée par défaut) : une image entre chaque paire, faite à partir des images seules, avec une demi-image de retard.",
				"   •  AMD : le launcher détecte la carte et utilise le moteur AMD de Belobug (VIctory-Belobug-AMD).",
				"",
				"TOUCHES EN JEU",
				"   •  F3 affiche/masque l'overlay : FPS, moyenne de la dernière minute (AVG 1M), moyenne depuis le début (ALL), pire demi-seconde (LOW).",
				"   •  Alt ou F1 libère la souris. La page Commandes a le reste."
			};
		}
		return new string[48]
		{
			"JOGAR",
			"   •  Predefinicoes: Recomendado (720p), Mais rapido e PC modesto (540p), Ultra baixo (PCs com muito pouca memoria), Nitido e",
			"      cgbj classico (1080p), Personalizado (os teus valores). O launcher escolhe uma pelo teu hardware na primeira vez.",
			"   •  Opcoes de arranque: passa o rato por cada uma para ver o que faz. A ★ marca o que foi testado.",
			"   •  JOGAR prepara as pipelines, abre o jogo e, quando fecha, guarda as pipelines desta sessao para a proxima.",
			"   •  Um jogo preso (um erro, uma janela que deixou de responder) nunca bloqueia o JOGAR: aparece 'FECHAR E JOGAR'.",
			"   •  Se faltar alguma coisa (memoria virtual, energia, Resizable BAR, shaders), um aviso amarelo diz o que e e leva-te a corrigir.",
			"",
			"PREPARAR",
			"   •  'Fazer tudo por mim' corre os passos que faltam: motor, pasta do jogo, shaders, energia,",
			"      memoria virtual, placa grafica certa para o VIctory e geracao de frames. O Windows pede administrador quando e preciso.",
			"   •  Resizable BAR: o launcher ve o que o D3D12 da ao jogo (nao so a BIOS) e mostra o guia da tua placa-mae.",
			"",
			"SHADERS",
			"   •  Mostra em tempo real os shaders traduzidos, as pipelines prontas, as que falharam, o espaco em disco e os erros.",
			"   •  'Reparar' refaz o que falta; 'Refazer tudo' apaga a cache e prepara de novo.",
			"",
			"DESEMPENHO",
			"   •  Quem ocupa a VRAM e a RAM; marca apps e fecha-as antes de jogar. Liga ou desliga a geracao de frames.",
			"",
			"HARDWARE E ID",
			"   •  O teu ID permanente (VI-XXXX-XXXX), com botao de copiar. Vem de pecas do PC e nao muda ao reinstalar o Windows.",
			"   •  Partilha de hardware (desligada por defeito): envia SO o hardware (placa, processador, RAM, placa-mae, Windows,",
			"      tipo de disco). Nunca envia logs (essas tem uma escolha propria), opcoes, caminhos, nomes nem o IP. A pagina mostra exatamente o que e enviado.",
			"",
			"ATUALIZACOES",
			"   •  O launcher verifica o GitHub ao abrir e de 5 em 5 minutos. Um update so e instalado se a assinatura e o SHA-256",
			"      baterem certo; instala-se sozinho quando o jogo nao esta a correr. E a unica coisa que o launcher recebe da internet.",
			"",
			"AJUSTES AVANCADOS (PERSONALIZADO)",
			"   •  Resolucao, texturas, buffers, protecoes de memoria e relogio de FPS. 'Copiar o meu perfil' da um codigo (VI1-...)",
			"      para partilhar; cola o de um amigo na caixa e carrega em 'Aplicar' (mostra o que muda antes).",
			"",
			"LOGS E ERROS",
			"   •  Logs do jogo: " + Path.Combine(dataRoot, "logs") + " (session-*.log, prepare.log).",
			"   •  Erros do launcher: " + Program.ErrorFile,
			"   •  'Enviar erros no Discord': escolhes 'erros do launcher' ou 'erros do jogo' e escreves o que aconteceu. O launcher junta os",
			"      ficheiros certos num zip no Ambiente de trabalho, abre a pasta e diz-te para o enviares no canal \"Send bugs here please\".",
			"      Nada e enviado sozinho: es tu que envias.",
			"",
			"PLACAS",
			"   •  NVIDIA RTX 40/50: Smooth Motion. RTX 30: NVSmooth30 (experimental). Outras placas (GTX 10, RTX 20, AMD, Intel): geracao de frames VI",
			"      (experimental, desligada por defeito): um frame entre cada dois do jogo, feito so com as imagens, com meio frame de atraso.",
			"   •  Todas as placas (NVIDIA, AMD, Intel) jogam com o motor edupode; para Radeon ha a alternativa do Belobug.",
			"",
			"TECLAS NO JOGO",
			"   •  F3 mostra/esconde o overlay: FPS, media do ultimo minuto (AVG 1M), media total (ALL), pior meio segundo (LOW).",
			"   •  Alt ou F1 solta o rato. A pagina Controlos tem o resto."
		};
	}

	private static IEnumerable<string> Credits()
	{
		return new string[10] { "   •  VIctory (Marvisak) — the Xbox Series emulator: github.com/Marvisak/VIctory", "   •  cgbj (cputroll/cgbj-reupload) — the performance build this one starts from", "   •  AnyXboxSeries — Xbox Series runtime work", "   •  Leonidas — fixes and research", "   •  VIctory-NVIDIA (trustno1-coder) — the lighting for NVIDIA cards", "   •  fxceless — rendering and performance fixes", "   •  VIctory-7900XTX (avatarmanz) — adaptations from the AMD fork", "   •  VIctory-Belobug-AMD (Belobug) — the alternative engine for Radeon cards: github.com/Belobug/VIctory-Belobug-AMD", "   •  NVSmooth30 (ItsAdeline) — Smooth Motion on RTX 30 cards (experimental): github.com/ItsAdeline/NVSmooth30", "   •  Cold Project VI (felicienradio) — the project page this remake follows" };
	}

	private void Refresh2()
	{
		if (exeBox != null && gameStatus != null && !exeBox.Visible)
		{
			exeBox.Top = gameStatus.Top - 2;
		}
		RefreshExes();
		bool flag = gameBox.Text.Trim().Length > 0 && File.Exists(GameExe);
		int num = ((exeBox != null && exeBox.Visible) ? exeBox.Items.Count : 0);
		Label label = gameStatus;
		string text;
		if (flag)
		{
			if (exeProblem != null)
			{
				text = "✗  " + exeProblem;
			}
			else
			{
				text = ((num > 1) ? string.Format(L("✓  Game found: {0} executables, choose one on the right.", "✓  Jogo encontrado: {0} executaveis, escolhe um a direita."), num) : (L("✓  Game found", "✓  Jogo encontrado", "✓  Jeu trouvé") + "  ·  " + Path.GetFileName(GameExe)));
			}
		}
		else
		{
			text = L("Choose the folder of your dump.", "Escolhe a pasta do teu dump.");
		}
		label.Text = text;
		Label label2 = gameStatus;
		Color foreColor;
		if (flag)
		{
			foreColor = ((exeProblem != null) ? Theme.Warn : Theme.Good);
		}
		else
		{
			foreColor = Theme.Dim;
		}
		label2.ForeColor = foreColor;
		gameHeading.Note = (flag ? Path.GetFileName(GameExe) : "");
		gameHeading.Invalidate();
		string victoryExe = VictoryExe;
		if (buildStatus != null)
		{
			Label label3 = buildStatus;
			string text2;
			if (victoryExe != null)
			{
				text2 = "✓  " + victoryExe;
			}
			else
			{
				text2 = (UseAmdEngine ? L("The AMD engine is not built yet: Setup > 'AMD engine (Belobug)'.", "O motor AMD ainda nao foi compilado: Preparar > 'Motor AMD (Belobug)'.") : L("Not built yet: press 'Build VIctory' (needs Rust and Visual Studio Build Tools).", "Ainda nao compilado: carrega em 'Compilar VIctory' (precisa do Rust e do Visual Studio Build Tools)."));
			}
			label3.Text = text2;
			buildStatus.ForeColor = ((victoryExe != null) ? Theme.Good : Theme.Warn);
		}
		modeInfo.Text = ((modeIndex < 0) ? "" : Selected.Description);
		presetHeading.Note = PresetSummary();
		presetHeading.Invalidate();
		GameState gameState = State();
		Anim.GameRunning = gameState != GameState.None || gameProc != null;
		Anim.Wake();
		bool flag2 = flag && victoryExe != null && gameState != GameState.Running && !busyNow && !setupBusy;
		playButton.Enabled = flag2;
		if (prepareButton != null)
		{
			prepareButton.Enabled = flag2 && gameState == GameState.None;
			prepareButton.Invalidate();
		}
		Pill pill = playButton;
		string text3;
		if (gameState == GameState.Running)
		{
			text3 = L("Running", "A correr", "En cours");
		}
		else if (busyNow)
		{
			text3 = L("Wait...", "Espera...", "Attends...");
		}
		else if (gameState == GameState.Stuck)
		{
			text3 = L("Close and play  →", "Fechar e jogar  →", "Fermer et jouer  →");
		}
		else if (setupBusy)
		{
			text3 = L("Setup is working...", "O Preparar esta a trabalhar...", "La préparation travaille...");
		}
		else if (flag)
		{
			text3 = ((victoryExe == null) ? L("Build VIctory first", "Compila o VIctory primeiro", "Compile VIctory d'abord") : L("Play  →", "Jogar  →", "Jouer  →"));
		}
		else
		{
			text3 = L("Choose the game first", "Escolhe o jogo primeiro", "Choisis le jeu d'abord");
		}
		pill.Text = text3;
		closeButton.Visible = gameState != GameState.None;
		closeButton.Text = ((gameState == GameState.Stuck) ? L("Close stuck game", "Fechar jogo preso") : L("Close game", "Fechar o jogo"));
		if (tips == null)
		{
			tips = new ToolTip();
		}
		string text4;
		if (flag2)
		{
			text4 = "";
		}
		else if (flag)
		{
			if (victoryExe == null)
			{
				text4 = L("Build VIctory first (Setup).", "Compila o VIctory primeiro (Preparar).");
			}
			else
			{
				text4 = ((gameState == GameState.Running) ? L("The game is already running ('Close game' ends it).", "O jogo ja esta a correr ('Fechar o jogo' termina-o).") : L("Wait for the current task to finish.", "Espera que a tarefa atual acabe."));
			}
		}
		else
		{
			text4 = L("Choose the game folder first (Auto-detect).", "Escolhe primeiro a pasta do jogo (Detetar).");
		}
		tips.SetToolTip(playButton, (gameState == GameState.Stuck) ? L("A VIctory session stopped answering: PLAY closes it and starts again.", "Uma sessao do VIctory deixou de responder: JOGAR fecha-a e arranca de novo.") : text4);
		playButton.Invalidate();
		closeButton.Invalidate();
		List<string> list = Missing();
		statusStrip.Items = new string[4]
		{
			(flag && exeProblem == null) ? L("Dump verified", "Dump verificado") : L("No game dump", "Sem dump do jogo"),
			(victoryExe != null) ? L("Binary ready", "Binario pronto") : L("Binary missing", "Falta o binario"),
			(shaderFiles > 0) ? string.Format(L("{0:N0} shaders cached", "{0:N0} shaders em cache"), shaderFiles) : L("Shaders not prepared", "Shaders por preparar"),
			(list.Count == 0) ? L("Windows ready", "Windows pronto") : string.Format(L("{0} to fix", "{0} por corrigir"), list.Count)
		};
		statusStrip.States = new int[4]
		{
			(flag && exeProblem == null) ? 1 : 0,
			(victoryExe != null) ? 1 : 0,
			(shaderFiles >= 1000) ? 1 : 0,
			(list.Count == 0) ? 1 : 0
		};
		statusStrip.Invalidate();
		if (list.Count > 0)
		{
			warnText.Text = "⚠  " + L("Running without: ", "A jogar sem: ") + string.Join(" · ", list.ToArray());
			warnText.ForeColor = Theme.Warn;
			warnFix.Visible = true;
		}
		else
		{
			warnText.Text = "✓  " + L("Windows settings, Resizable BAR and shaders are all set.", "Definicoes do Windows, Resizable BAR e shaders: tudo pronto.");
			warnText.ForeColor = Theme.Dim;
			warnFix.Visible = false;
		}
		warnPanel.Invalidate();
		Color stateColor = gameState switch
		{
			GameState.Stuck => Theme.Warn, 
			GameState.Running => Theme.Cyan, 
			_ => busyNow ? Theme.Cyan : (flag2 ? Theme.Good : Theme.Warn), 
		};
		string text5 = gameState switch
		{
			GameState.Stuck => L("Stuck", "Preso", "Bloqué"), 
			GameState.Running => L("Running", "A correr", "En cours"), 
			_ => busyNow ? L("Working", "A trabalhar", "En cours de travail") : (flag2 ? L("Ready", "Pronto", "Prêt") : L("Not ready", "Nao esta pronto", "Pas prêt")), 
		};
		footMeta.Set(text5, stateColor, $"{Selected.Name} ({(Selected.Custom ? CustomTag() : Selected.Tag)})", EngineName, $"{vramGb:0} GB VRAM  ·  {ramGb:0} GB RAM", (!flag2 && text4.Length > 0 && gameState != GameState.Running) ? text4 : "");
		bool flag3 = statusStrip.States.All((int v) => v == 1);
		banner.Steps = statusStrip.Items;
		banner.States = statusStrip.States;
		ref string title = ref banner.Title;
		string text6;
		if (gameState == GameState.Running)
		{
			text6 = L("Playing", "A jogar", "En jeu");
		}
		else
		{
			text6 = (flag3 ? L("Ready", "Pronto", "Prêt") : L("Almost", "Quase", "Presque"));
		}
		title = text6;
		ref string accent = ref banner.Accent;
		string text7;
		if (gameState == GameState.Running)
		{
			text7 = L("right now.", "agora mesmo.", "en ce moment.");
		}
		else
		{
			text7 = (flag3 ? L("when you are.", "quando quiseres.", "quand tu veux.") : L("there.", "la.", "là."));
		}
		accent = text7;
		ref string status = ref banner.Status;
		string text8;
		if (gameState == GameState.Running)
		{
			text8 = L("The game is running. Close it from the bar below when you are done.", "O jogo esta a correr. Fecha-o na barra em baixo quando acabares.", "Le jeu tourne. Ferme-le depuis la barre en bas quand tu as fini.");
		}
		else if (busyNow || setupBusy)
		{
			text8 = L("A task is running. Play unlocks when it finishes.", "Uma tarefa esta a correr. O Jogar liberta-se quando acabar.", "Une tâche est en cours. Jouer se débloque quand elle finit.");
		}
		else if (flag2 && list.Count == 0)
		{
			text8 = L("Game, engine, shaders and Windows are all set. Pick a preset below, or just press play.", "Jogo, motor, shaders e Windows estao prontos. Escolhe uma predefinicao em baixo, ou carrega em jogar.", "Jeu, moteur, shaders et Windows sont prêts. Choisis un préréglage plus bas, ou lance simplement.");
		}
		else
		{
			text8 = (flag2 ? (L("Ready to launch, but running without: ", "Pronto a arrancar, mas a jogar sem: ", "Prêt à lancer, mais sans : ") + string.Join(" · ", list.ToArray())) : text4);
		}
		status = text8;
		heroGhost.Text = ((!flag3 || list.Count > 0) ? L("Fix it", "Corrigir", "Corriger") : L("Setup", "Preparar", "Préparer"));
		heroGhost.Invalidate();
		banner.Invalidate();
	}

	private string EffectiveScale(string scale)
	{
		if (OutputHeight != 720)
		{
			return scale;
		}
		double result = 1.0;
		if (scale != null)
		{
			double.TryParse(scale, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
		}
		if (result <= 0.0)
		{
			result = 1.0;
		}
		return Math.Min(result, 2.0 / 3.0).ToString("0.#######", CultureInfo.InvariantCulture);
	}

	private void EngineFallback(bool failedEarly)
	{
		if (!failedEarly)
		{
			if (ReadSetting("edupode_fails") != "0")
			{
				WriteSetting("edupode_fails", "0");
			}
		}
		else
		{
			if (UseAmdEngine || !hasRadeon || hasNvidia)
			{
				return;
			}
			int.TryParse(ReadSetting("edupode_fails") ?? "0", out var result);
			result++;
			WriteSetting("edupode_fails", result.ToString());
			if (result >= 2)
			{
				WriteSetting("edupode_fails", "0");
				WriteSetting("engine", "amd");
				WriteSetting("engine_auto", "1");
				try
				{
					BeginInvoke((Action)(() =>
					{
						if (engineBox != null)
						{
							engineBox.SelectedIndex = 1;
						}
						UpdateBanner();
						Refresh2();
						Log(L("The edupode engine ended at start twice on this Radeon: switched to Belobug's AMD engine.", "O motor edupode fechou ao arrancar duas vezes nesta Radeon: mudei para o motor AMD do Belobug."));
						MessageBox.Show(this, L("The edupode engine did not start twice in a row on your Radeon, so the launcher switched to Belobug's AMD engine (VIctory-Belobug-AMD, all credit to Belobug). If it is not built yet, Setup builds it now; then press PLAY.\r\n\r\nTo try the edupode engine again: Hardware & ID > Engine. A report ('Send errors') helps us make it start on your card.", "O motor edupode nao arrancou duas vezes seguidas na tua Radeon, por isso o launcher mudou para o motor AMD do Belobug (VIctory-Belobug-AMD, todo o credito para o Belobug). Se ainda nao estiver compilado, o Preparar compila-o agora; depois carrega em JOGAR.\r\n\r\nPara experimentar o motor edupode outra vez: Hardware e ID > Motor. Um relatorio ('Enviar erros') ajuda-nos a pô-lo a arrancar na tua placa."), "Julia's Launcher", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
						if (VictoryExe == null)
						{
							Show(4);
							RunSteps(steps.Where((Step st) => st.Fix == new Func<bool>(BuildAmdEngine)).ToArray());
						}
					}));
					return;
				}
				catch (Exception)
				{
					return;
				}
			}
			Log(L("The edupode engine ended at start on this Radeon (1 of 2): one more and the launcher moves to Belobug's AMD engine.", "O motor edupode fechou ao arrancar nesta Radeon (1 de 2): mais uma e o launcher passa para o motor AMD do Belobug."));
		}
	}

	private string PresetSummary()
	{
		if (modeIndex < 0)
		{
			return "";
		}
		Mode selected = Selected;
		string text = EffectiveScale(selected.Custom ? custom.Scale : selected.Scale);
		string text2 = ((text == null || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || !(result < 0.999)) ? string.Format(L("native {0}p", "{0}p nativo"), DisplayHeight) : string.Format(L("{0}×{1} internal upscaled to {2}p", "{0}×{1} interno ampliado para {2}p"), (int)Math.Round((double)DisplayWidth * result), (int)Math.Round((double)DisplayHeight * result), DisplayHeight));
		bool flag = (selected.Custom ? (custom.Fps == 60) : (fps60Box.Checked && !selected.UltraLow));
		return text2 + " · " + (flag ? L("60 FPS clock", "relogio de 60 FPS") : L("30 FPS clock", "relogio de 30 FPS"));
	}

	private void UpdateBanner()
	{
		if (showEnginePills != null)
		{
			showEnginePills();
		}
		if (banner == null)
		{
			return;
		}
		List<string> labels = new List<string>();
		List<string> values = new List<string>();
		List<Color> colors = new List<Color>();
		Action<string, string, Color> action = (string l, string v, Color c) =>
		{
			labels.Add(l);
			values.Add(v);
			colors.Add(c);
		};
		action(L("Graphics card", "Placa grafica", "Carte graphique"), gpuName, Theme.Muted);
		action(L("Video memory", "Memoria de video", "Mémoire vidéo"), $"{vramGb:0} GB", (vramGb >= 10.0) ? Theme.Muted : Theme.Warn);
		action("RAM", $"{ramGb:0} GB", (ramGb >= 24.0) ? Theme.Muted : Theme.Warn);
		string text = RebarText();
		action("Resizable BAR", text.StartsWith("ReBAR ") ? text.Substring(6) : text, rebar ? Theme.Muted : Theme.Warn);
		string text2 = FrameGenChip();
		string[] array = new string[2] { "Frame gen ", "Geracao de frames " };
		foreach (string text3 in array)
		{
			if (text2.StartsWith(text3))
			{
				text2 = text2.Substring(text3.Length);
			}
		}
		action(L("Frame generation", "Geracao de frames", "Génération d'images"), text2, Theme.Muted);
		action(L("Engine", "Motor", "Moteur"), UseAmdEngine ? "AMD · Belobug" : L("Standard", "Padrao", "Standard"), UseAmdEngine ? Theme.Cyan : Theme.Muted);
		if (smoothBox is OptionCard optionCard)
		{
			optionCard.Line = FrameGenLine();
			optionCard.Enabled = FrameGenAvailable;
			optionCard.Invalidate();
		}
		banner.FactLabels = labels.ToArray();
		banner.FactValues = values.ToArray();
		banner.FactColors = colors.ToArray();
		banner.Invalidate();
	}

	private string RebarText()
	{
		if (rebarHeap == 1)
		{
			return L("ReBAR On", "ReBAR ligado");
		}
		if (rebarHeap == 0 && rebarBios)
		{
			return L("ReBAR On (not used by Windows)", "ReBAR ligado (o Windows nao o usa)");
		}
		if (rebarBios)
		{
			return L("ReBAR On", "ReBAR ligado");
		}
		if (!integratedOnly && RebarCardSupport() == 0)
		{
			return L("ReBAR n/a (card)", "ReBAR n/d (placa)");
		}
		if (RebarMissing())
		{
			return L("ReBAR Off", "ReBAR desligado");
		}
		if (rebarHeap != 0)
		{
			return L("ReBAR ?", "ReBAR ?");
		}
		return L("ReBAR not offered by Windows", "ReBAR nao oferecido pelo Windows");
	}

	private void DetectSystem()
	{
		try
		{
			string text = RunCapture(NvidiaSmi, "--query-gpu=name,memory.total,driver_version --format=csv,noheader,nounits");
			if (!string.IsNullOrEmpty(text) && text.Contains(","))
			{
				string text2 = null;
				double num = -1.0;
				string[] array = text.Split('\n');
				foreach (string text3 in array)
				{
					string[] array2 = text3.Split(',');
					if (array2.Length >= 2 && double.TryParse(array2[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && result > num)
					{
						num = result;
						text2 = text3;
					}
				}
				if (text2 != null)
				{
					string[] array3 = text2.Split(',');
					gpuName = array3[0].Trim().Replace("NVIDIA GeForce ", "");
					vramGb = num / 1024.0;
					if (array3.Length > 2)
					{
						driverVersion = array3[2].Trim();
					}
				}
				hasNvidia = true;
				string input = RunCapture(NvidiaSmi, "-q -d MEMORY");
				foreach (Match item in Regex.Matches(input, "BAR1 Memory Usage\\s+Total\\s*:\\s*(\\d+)\\s*MiB", RegexOptions.IgnoreCase))
				{
					if (double.TryParse(item.Groups[1].Value, out var result2))
					{
						barRead = true;
						if (result2 >= 1024.0)
						{
							rebarBios = true;
						}
					}
				}
				canSmooth = Regex.IsMatch(gpuName, "RTX\\s*(40|50)\\d\\d");
				rtxFrameGen = canSmooth && (FgChoice == "auto" || FgChoice == "smooth");
			}
			ComputerInfo computerInfo = new ComputerInfo();
			ramGb = (double)computerInfo.TotalPhysicalMemory / 1073741824.0;
		}
		catch (Exception)
		{
		}
		DetectCards();
		FillFgBox();
		rebar = rebarBios;
		if (!FrameGenAvailable)
		{
			smoothBox.Checked = false;
		}
		if (integratedOnly && ReadSetting("igpu_defaults") != "1")
		{
			fightsBox.Checked = false;
			smoothBox.Checked = false;
			fps60Box.Checked = false;
			SelectMode(5);
			WriteSetting("igpu_defaults", "1");
		}
		else if (OwnFrameGen && ReadSetting("smooth") == null)
		{
			smoothBox.Checked = false;
		}
		if (modeIndex < 0)
		{
			SelectMode((integratedOnly || (ramGb > 0.0 && ramGb <= 16.5) || (vramGb > 0.0 && vramGb <= 6.5)) ? 5 : (((vramGb > 0.0 && vramGb < 10.0) || (ramGb > 0.0 && ramGb < 24.0)) ? 2 : 0));
		}
		UpdateBanner();
		UpdateSmooth();
	}

	private static string RunCapture(string file, string args)
	{
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo(file, args);
			processStartInfo.UseShellExecute = false;
			processStartInfo.RedirectStandardOutput = true;
			processStartInfo.CreateNoWindow = true;
			using Process process = Process.Start(processStartInfo);
			StringBuilder sb = new StringBuilder();
			process.OutputDataReceived += (object s, DataReceivedEventArgs e) =>
			{
				if (e.Data != null)
				{
					lock (sb)
					{
						sb.Append(e.Data).Append("\r\n");
					}
				}
			};
			process.BeginOutputReadLine();
			if (!process.WaitForExit(5000))
			{
				try
				{
					process.Kill();
				}
				catch (Exception)
				{
				}
				return "";
			}
			process.WaitForExit(2000);
			lock (sb)
			{
				return sb.ToString();
			}
		}
		catch (Exception)
		{
			return "";
		}
	}

	private bool ClosingNow()
	{
		if (closePrompt && !closing && InvokeRequired)
		{
			for (int i = 0; i < 60; i++)
			{
				if (!closePrompt)
				{
					break;
				}
				if (closing)
				{
					break;
				}
				if (IsDisposed)
				{
					break;
				}
				Thread.Sleep(50);
			}
		}
		if (!closing && !IsDisposed)
		{
			return Disposing;
		}
		return true;
	}

	private void Post(Action a)
	{
		try
		{
			if (!IsDisposed && !Disposing && IsHandleCreated)
			{
				BeginInvoke(a);
			}
		}
		catch (Exception)
		{
		}
	}

	private void Log(string line)
	{
		try
		{
			if (!IsDisposed)
			{
				if (InvokeRequired)
				{
					BeginInvoke(new Action<string>(Log), line);
				}
				else
				{
					logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + line + "\r\n");
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private static void WriteLinesAtomic(string path, string[] lines)
	{
		string text = path + ".tmp";
		try
		{
			File.WriteAllLines(text, lines);
			if (File.Exists(path))
			{
				File.Replace(text, path, null);
			}
			else
			{
				File.Move(text, path);
			}
		}
		catch (Exception)
		{
			try
			{
				File.Delete(text);
			}
			catch (Exception)
			{
			}
			throw;
		}
	}

	private Dictionary<string, string> Environment2()
	{
		if (UseAmdEngine)
		{
			return AmdEnvironment();
		}
		Dictionary<string, string> e = new Dictionary<string, string>();
		Action<string, string> action = (string k, string v) =>
		{
			e[k] = v;
		};
		action("VICTORY_GAME_DIR", gameBox.Text.Trim());
		action("VICTORY_CACHE_DIR", cacheDir);
		action("VICTORY_DATA_DIR", dataDir);
		action("VICTORY_LOG", "warn,victory_gpu::host=info,victory_gpu::copystat=info,victory_gpu::framegen=info");
		action("VICTORY_DISPLAY_MODE", DisplayWidth + "x" + DisplayHeight);
		action("VICTORY_CARD_BUFFERS_MB", "1024");
		action("VICTORY_PIPELINE_BOOK", "1");
		action("VICTORY_RIPE_BLOCKS", "4");
		action("VICTORY_PRESSURE_FREE_MB", "1024");
		action("VICTORY_PRESSURE_CLEAR_MB", "2048");
		action("VICTORY_TEXTURE_RAM_GUARD", "0");
		action("VICTORY_CARD_ROOM_FOR_TARGETS_MB", "1792");
		action("VICTORY_AMBIENT", "1.6");
		action("VICTORY_TEXTURE_NO_WAIT", "1");
		action("VICTORY_RELAY", "2");
		action("VICTORY_WIDE_BACK", "1");
		action("VICTORY_CARD_REHASH_RUNS", "1");
		action("VICTORY_JOIN_COPIES", "1");
		action("VICTORY_INDIRECT_WINDOW", "1");
		action("VICTORY_RELAY_COPY", "1");
		action("VICTORY_CARD_STEPS", "2");
		action("VICTORY_INDIRECT_BATCH", "2");
		action("VICTORY_MAP_THREADS", "1");
		action("VICTORY_CARD_AWAKE", "1");
		action("VICTORY_SHOW_AFTER", "1");
		action("VICTORY_THIN", "1");
		action("VICTORY_JOIN_PARTS", "8");
		action("VICTORY_UPLOAD_AHEAD", "1");
		action("VICTORY_LEVELS_ON_DEMAND", "1");
		action("VICTORY_CARD_BUFFERS_MIN_KB", "64");
		action("VICTORY_BVH_MISS", "1");
		action("VICTORY_AMBIENT_AIR", "1.5");
		action("VICTORY_AMBIENT_MOST", "2");
		action("VICTORY_NAN_SCRUB", "1");
		action("VICTORY_INF_AS", "65504");
		action("VICTORY_DYNAMIC_SLOTS", "524288");
		action("VICTORY_WAVE_WIDE_DIGESTS", "7099a57fded142613b44f410463d6be6");
		string text = Path.Combine(SourceDir ?? root, "scripts", "rt_off_hashes.txt");
		if (File.Exists(text))
		{
			action("VICTORY_SKIP_PIPELINES", "@" + text);
		}
		Mode selected = Selected;
		if (selected.Scale != null)
		{
			action("VICTORY_RENDER_SCALE", selected.Scale);
			action("VICTORY_OWN_UPSCALER", "1");
		}
		if (selected.LightTextures)
		{
			action("VICTORY_TEXTURE_MAX", "2048");
			action("VICTORY_LEVELS_ON_DEMAND", "0");
			action("VICTORY_CARD_BUFFERS_MB", "1536");
			action("VICTORY_POOL_FULLEST", "1");
			action("VICTORY_TEXTURE_STAGE_MB", "384");
			if (rebar)
			{
				action("VICTORY_WINDOWS_BAR_MB", "1024");
			}
		}
		if (selected.Modest || integratedOnly || (vramGb > 0.0 && vramGb < 10.0))
		{
			action("VICTORY_CARD_BUFFERS_MB", "768");
			action("VICTORY_TEXTURE_MAX", "1024");
			action("VICTORY_TEXTURE_STAGE_MB", "128");
			action("VICTORY_RIPE_BLOCKS", "2");
			action("VICTORY_CARD_ROOM_FOR_TARGETS_MB", "1024");
			e.Remove("VICTORY_WINDOWS_BAR_MB");
		}
		if (selected.Modest || (ramGb > 0.0 && ramGb < 24.0))
		{
			e.Remove("VICTORY_PRESSURE_FREE_MB");
			e.Remove("VICTORY_PRESSURE_CLEAR_MB");
			e.Remove("VICTORY_TEXTURE_RAM_GUARD");
		}
		if (selected.UltraLow)
		{
			action("VICTORY_CARD_BUFFERS_MB", "512");
			action("VICTORY_TEXTURE_MAX", "512");
			action("VICTORY_TEXTURE_STAGE_MB", "64");
			action("VICTORY_RIPE_BLOCKS", "2");
			action("VICTORY_CARD_ROOM_FOR_TARGETS_MB", "768");
			action("VICTORY_CARD_BUFFERS_MIN_KB", "128");
			action("VICTORY_LEVELS_ON_DEMAND", "1");
			e.Remove("VICTORY_POOL_FULLEST");
			e.Remove("VICTORY_WINDOWS_BAR_MB");
		}
		if (fightsBox.Checked && !selected.Modest && !selected.UltraLow && !integratedOnly && vramGb >= 11.0)
		{
			action("VICTORY_CARD_BUFFERS_MB", "2048");
		}
		if (panelBox.Checked)
		{
			action("VICTORY_PANEL", "1");
		}
		if (borderlessBox.Checked)
		{
			action("VICTORY_BORDERLESS", "1");
			action("VICTORY_BORDERLESS_DPI", "0");
		}
		if (fps60Box.Checked && !selected.UltraLow)
		{
			action("VICTORY_FPS_MAX", "60");
		}
		if (smoothBox.Checked && OwnFrameGen)
		{
			action("VICTORY_FRAMEGEN", "1");
		}
		if (selected.Custom)
		{
			ApplyCustom(e);
		}
		if (OutputHeight == 720)
		{
			string text2 = EffectiveScale(e.TryGetValue("VICTORY_RENDER_SCALE", out var value) ? value : null);
			if (text2 == null)
			{
				e.Remove("VICTORY_RENDER_SCALE");
				e.Remove("VICTORY_OWN_UPSCALER");
			}
			else
			{
				action("VICTORY_RENDER_SCALE", text2);
				action("VICTORY_OWN_UPSCALER", "1");
			}
		}
		action("VICTORY_VRAM_RESERVE", reserveBox.Checked ? "max" : "0");
		if (cpuBox.Checked)
		{
			action("VICTORY_WIDE_THREADS", Math.Max(2, Math.Min(12, Environment.ProcessorCount / 2)).ToString());
			action("VICTORY_ASK_EARLY", "2");
		}
		if (facesBox.Checked)
		{
			action("VICTORY_TEXTURE_NO_WAIT", "0");
		}
		if (loadingBox.Checked)
		{
			action("VICTORY_BAKE_NO_WAIT", "1");
		}
		Action<string, string> action2 = action;
		string arg;
		if (smoothBox.Checked)
		{
			if (rtxFrameGen)
			{
				arg = "SMOOTH MOTION";
			}
			else if (NvSmoothHere)
			{
				arg = "NVSMOOTH30";
			}
			else
			{
				arg = (OwnFrameGen ? "VI 2X" : "OFF");
			}
		}
		else
		{
			arg = "OFF";
		}
		action2("VICTORY_PANEL_FRAMEGEN", arg);
		ApplyTestEnv(e);
		return e;
	}

	private void ApplyTestEnv(Dictionary<string, string> e)
	{
		try
		{
			string path = Path.Combine(Path.GetDirectoryName(dataRoot), "test-env.txt");
			if (!File.Exists(path))
			{
				return;
			}
			string[] array = File.ReadAllLines(path);
			foreach (string text in array)
			{
				string text2 = text.Trim();
				if (text2.Length == 0 || text2.StartsWith("#"))
				{
					continue;
				}
				int num = text2.IndexOf('=');
				if (num > 0)
				{
					string text3 = text2.Substring(0, num).Trim();
					if (text3.StartsWith("VICTORY_") && !(text3 == "VICTORY_GAME_DIR") && !(text3 == "VICTORY_DATA_DIR"))
					{
						e[text3] = text2.Substring(num + 1).Trim();
					}
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private Process Start(string args, string logFile)
	{
		return Start(args, logFile, null);
	}

	private Process Start(string args, string logFile, Action<string> onLine)
	{
		if (ClosingNow())
		{
			throw new InvalidOperationException(L("The launcher is closing.", "O launcher esta a fechar.", "Le lanceur se ferme."));
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo(VictoryExe, args);
		processStartInfo.UseShellExecute = false;
		processStartInfo.RedirectStandardOutput = true;
		processStartInfo.RedirectStandardError = true;
		processStartInfo.CreateNoWindow = true;
		processStartInfo.WorkingDirectory = (UseAmdEngine ? AmdEngineDir : root);
		ProcessStartInfo processStartInfo2 = processStartInfo;
		foreach (KeyValuePair<string, string> item in Environment2())
		{
			processStartInfo2.EnvironmentVariables[item.Key] = item.Value;
		}
		Directory.CreateDirectory(Path.GetDirectoryName(logFile));
		StreamWriter writer = new StreamWriter(logFile, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
		{
			AutoFlush = true
		};
		Process process = new Process();
		process.StartInfo = processStartInfo2;
		process.EnableRaisingEvents = true;
		Process process2 = process;
		DataReceivedEventHandler value = (object s, DataReceivedEventArgs e) =>
		{
			if (e.Data != null)
			{
				lock (writer)
				{
					writer.WriteLine(e.Data);
				}
				if (onLine != null)
				{
					onLine(e.Data);
				}
			}
		};
		process2.OutputDataReceived += value;
		process2.ErrorDataReceived += value;
		process2.Exited += (object s, EventArgs e) =>
		{
			lock (writer)
			{
				writer.Dispose();
			}
		};
		process2.Start();
		process2.BeginOutputReadLine();
		process2.BeginErrorReadLine();
		if (!args.StartsWith("run "))
		{
			lock (tools)
			{
				tools.RemoveAll((Process t) =>
				{
					try
					{
						return t.HasExited;
					}
					catch (Exception)
					{
						return true;
					}
				});
				tools.Add(process2);
			}
		}
		return process2;
	}

	private void Busy(bool busy)
	{
		if (InvokeRequired)
		{
			BeginInvoke(new Action<bool>(Busy), busy);
			return;
		}
		busyNow = busy;
		playButton.Enabled = !busy;
		prepareButton.Enabled = !busy;
		buildButton.Enabled = !busy;
		playButton.Invalidate();
		prepareButton.Invalidate();
		buildButton.Invalidate();
		if (!busy)
		{
			Refresh2();
		}
	}

	private void Play()
	{
		if (integratedOnly && ReadSetting("igpu_ack") != "1")
		{
			string body = L("Integrated graphics (Intel Iris Xe / UHD, AMD Radeon Graphics) found, and no graphics card of its own.\r\n\r\nThe game is very likely to crash or miss graphics: integrated graphics borrow the PC's RAM, which the game already needs, and the driver refuses some of its shaders. A graphics card with 4 GB or more of its own memory is needed.\r\n\r\nThe launcher has picked Ultra Low with the heavy options off. You can still try.", "Foi encontrada uma grafica integrada (Intel Iris Xe / UHD, AMD Radeon Graphics) e nenhuma placa grafica dedicada.\r\n\r\nE muito provavel que o jogo feche ou que faltem graficos: a grafica integrada usa a RAM do PC, de que o jogo ja precisa, e o driver recusa alguns shaders. E preciso uma placa grafica com 4 GB ou mais de memoria propria.\r\n\r\nO launcher escolheu o Ultra baixo com as opcoes pesadas desligadas. Podes tentar na mesma.", "Graphique intégré trouvé (Intel Iris Xe / UHD, AMD Radeon Graphics), sans carte graphique dédiée.\r\n\r\nLe jeu risque fort de planter ou d'avoir des graphismes manquants : le graphique intégré emprunte la RAM du PC, dont le jeu a déjà besoin, et le pilote refuse certains shaders. Il faut une carte graphique avec 4 Go ou plus de mémoire à elle.\r\n\r\nLe launcher a choisi Ultra bas avec les options lourdes désactivées. Tu peux essayer quand même.");
			int num = Notice(L("Integrated graphics", "Grafica integrada", "Graphique intégré"), body, L("Play anyway", "Jogar na mesma", "Jouer quand même"), L("Cancel", "Cancelar", "Annuler"));
			if (num != 1)
			{
				return;
			}
			WriteSetting("igpu_ack", "1");
		}
		if (setupBusy)
		{
			MessageBox.Show(this, L("Setup is still working (building VIctory or preparing shaders). PLAY opens when it finishes.", "O Preparar ainda esta a trabalhar (a compilar o VIctory ou a preparar shaders). O JOGAR abre quando acabar."), "Julia's Launcher");
		}
		else
		{
			if (!ClearOtherPreparations())
			{
				return;
			}
			SaveSettings();
			switch (State())
			{
			case GameState.Running:
				if (MessageBox.Show(this, L("VIctory is already running. Close it and start again?", "O VIctory ja esta a correr. Fechar e arrancar de novo?"), "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
				{
					return;
				}
				CloseGames();
				break;
			case GameState.Stuck:
				Log(L("The last session had stopped answering: closing it.", "A ultima sessao tinha deixado de responder: a fecha-la."));
				CloseGames();
				break;
			}
			if (afterWarm != null && !afterWarm.HasExited)
			{
				try
				{
					RunWait("taskkill", "/PID " + afterWarm.Id + " /T /F", window: false);
				}
				catch (Exception)
				{
				}
			}
			afterWarm = null;
			if (exeProblem != null)
			{
				MessageBox.Show(this, exeProblem, "Julia's Launcher", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return;
			}
			List<string> list = Missing();
			if (list.Count > 0 && !warnedThisSession)
			{
				string text = L("The game will run worse (or close) without these:\r\n\r\n", "O jogo vai correr pior (ou fechar) sem isto:\r\n\r\n") + string.Join("\r\n", list.Select((string x) => "  •  " + x).ToArray()) + L("\r\n\r\nYes: play anyway.   No: open Setup to fix them.", "\r\n\r\nSim: jogar na mesma.   Nao: abrir o Preparar para corrigir.");
				if (MessageBox.Show(this, text, "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
				{
					Show(4);
					return;
				}
				warnedThisSession = true;
			}
			if (!EnoughMemory())
			{
				Show(1);
				return;
			}
			AskToShare();
			AskSessionLogs();
			if (!HeavyAppsCheck())
			{
				return;
			}
			PipelineHint();
			BackupSavesBeforePlay();
			GpuUsers(out var cardUsed);
			if (cardUsed > 2147483648u)
			{
				Log(string.Format(L("Other apps already hold {0:0.0} GB of the card: the Performance page frees it.", "Outras apps ja ocupam {0:0.0} GB da placa: a pagina Desempenho liberta-a."), (double)cardUsed / 1073741824.0));
			}
			ApplyFrameGen();
			string text2 = DateTime.Now.ToString("yyyyMMdd-HHmmss");
			string log = Path.Combine(dataRoot, "logs", "session-" + text2 + ".log");
			Log(string.Concat(L("Starting ", "A iniciar "), Selected, ". Log: ", log));
			Busy(busy: true);
			string game = GameExe;
			bool warmFirst = warmBox.Checked;
			Thread thread = new Thread(() =>
			{
				try
				{
					if (warmFirst)
					{
						WarmWithBar(log);
					}
					if (!ClosingNow())
					{
						DateTime now = DateTime.Now;
						Process process = Start("run \"" + game + "\" --console series-s --execute", log);
						gameProc = process;
						gameHadWindow = false;
						windowGone = null;
						Anim.GameRunning = true;
						WatchStart(log, process);
						Busy(busy: false);
						process.WaitForExit();
						RemoveNvSmooth();
						int num2 = 0;
						try
						{
							num2 = process.ExitCode;
						}
						catch (Exception)
						{
						}
						SendSessionLog(log, num2, now, DateTime.Now);
						if (gameProc == process)
						{
							gameProc = null;
							if (num2 != 0)
							{
								Log(string.Format(L("The game ended with an error (code {0}). Log: {1}", "O jogo terminou com um erro (codigo {0}). Log: {1}"), num2, log));
							}
							EngineFallback(num2 != 0 && (DateTime.Now - now).TotalMinutes < 3.0);
							if (!ClosingNow())
							{
								Log(L("Game closed. Preparing this session's pipelines for the next one...", "Jogo fechado. A preparar as pipelines desta sessao para a proxima..."));
								Process process2 = Start("warm --used", log);
								afterWarm = process2;
								BeginInvoke(new Action(Refresh2));
								if (!process2.WaitForExit(600000))
								{
									try
									{
										RunWait("taskkill", "/PID " + process2.Id + " /T /F", window: false);
									}
									catch (Exception)
									{
									}
								}
								if (afterWarm == process2)
								{
									afterWarm = null;
									Log(L("Done.", "Feito."));
								}
							}
						}
					}
				}
				catch (Exception ex4)
				{
					Log(L("Could not start: ", "Nao arrancou: ") + ex4.Message + (ex4.Message.Contains("denied") ? L("  (Defender: see Help)", "  (Defender: ve a Ajuda)") : ""));
				}
				finally
				{
					try
					{
						if (busyNow)
						{
							Busy(busy: false);
						}
					}
					catch (Exception)
					{
					}
					Post(Refresh2);
				}
			});
			thread.IsBackground = true;
			thread.Start();
		}
	}

	private void WarmWithBar(string log)
	{
		int total = 0;
		BeginInvoke((Action)(() =>
		{
			if (!loadBar.Visible && loadBarAnchor != null)
			{
				loadBar.Top = loadBarAnchor.Top + 42;
			}
			loadBar.Visible = true;
			loadBar.Set(0f, L("Loading pipelines...", "A carregar pipelines..."));
		}));
		Process process = Start("warm --used", log, (string line) =>
		{
			Match match = Regex.Match(line, "; (\\d+) to make");
			if (match.Success)
			{
				total = int.Parse(match.Groups[1].Value);
			}
			Match match2 = Regex.Match(line, "^(\\d+) of (\\d+), (\\d+) s");
			if (match2.Success)
			{
				int n = int.Parse(match2.Groups[1].Value);
				int of = int.Parse(match2.Groups[2].Value);
				BeginInvoke((Action)(() =>
				{
					loadBar.Set((of > 0) ? ((float)n / (float)of) : 1f, string.Format(L("Loading pipelines: {0} of {1}", "A carregar pipelines: {0} de {1}"), n, of));
				}));
				Stage(1, (of > 0) ? ((float)n / (float)of) : 1f, string.Format(L("Pipelines: {0:N0} of {1:N0} loaded before the game · {2} s", "Pipelines: {0:N0} de {1:N0} carregadas antes do jogo · {2} s"), n, of, match2.Groups[3].Value), live: true);
			}
			else if (match.Success)
			{
				BeginInvoke((Action)(() =>
				{
					loadBar.Set((total == 0) ? 1f : 0f, (total == 0) ? L("Pipelines ready.", "Pipelines prontas.") : string.Format(L("Loading pipelines: 0 of {0}", "A carregar pipelines: 0 de {0}"), total));
				}));
			}
		});
		process.WaitForExit();
		Stage(1, 1f, "", live: false);
		Log((total == 0) ? L("Pipelines were already ready.", "As pipelines ja estavam prontas.") : string.Format(L("{0} pipelines loaded.", "{0} pipelines carregadas."), total));
		BeginInvoke((Action)(() =>
		{
			loadBar.Set(1f, L("Starting the game...", "A abrir o jogo..."));
		}));
		Thread.Sleep(600);
		BeginInvoke((Action)(() =>
		{
			loadBar.Visible = false;
		}));
	}

	private void Prepare()
	{
		RunSteps(steps.Where((Step st) => st.Fix == new Func<bool>(PrepareBlocking)).ToArray());
	}

	private void Build()
	{
		Show(4);
		List<Step> list = steps.Take(4).Where((Step st) =>
		{
			int num;
			try
			{
				num = st.Check();
			}
			catch (Exception)
			{
				num = 0;
			}
			return num == 0;
		}).ToList();
		Step item = steps.First((Step st) => st.Fix == (UseAmdEngine ? new Func<bool>(BuildAmdEngine) : new Func<bool>(BuildBlocking)));
		if (!list.Contains(item))
		{
			list.Add(item);
		}
		RunSteps(list.ToArray());
	}

	private string ReadSetting(string key)
	{
		try
		{
			lock (settingsLock)
			{
				if (!File.Exists(settingsFile))
				{
					return null;
				}
				string[] array = File.ReadAllLines(settingsFile);
				foreach (string text in array)
				{
					if (text.StartsWith(key + "="))
					{
						return Unmangle(text.Substring(key.Length + 1));
					}
				}
			}
		}
		catch (Exception)
		{
		}
		return null;
	}

	private static string Unmangle(string s)
	{
		if (s == null)
		{
			return null;
		}
		Encoding encoding = Encoding.GetEncoding(1252);
		for (int i = 0; i < 4; i++)
		{
			if (s.IndexOf('Ã') < 0 && s.IndexOf('Â') < 0)
			{
				break;
			}
			string text = Encoding.UTF8.GetString(encoding.GetBytes(s));
			if (text == s || text.IndexOf('\ufffd') >= 0)
			{
				break;
			}
			s = text;
		}
		return s;
	}

	private string DefaultCacheDir()
	{
		string text = Path.Combine(dataRoot, "cache");
		if (!text.All((char c) => c < '\u0080'))
		{
			return Path.Combine(Path.GetPathRoot(dataRoot), "VIctory", "cache");
		}
		return text;
	}

	private void LoadSettings()
	{
		try
		{
			if (!File.Exists(settingsFile))
			{
				return;
			}
			string[] array = File.ReadAllLines(settingsFile);
			foreach (string text in array)
			{
				int num = text.IndexOf('=');
				if (num < 0)
				{
					continue;
				}
				string text2 = text.Substring(0, num);
				string text3 = Unmangle(text.Substring(num + 1));
				switch (text2)
				{
				case "game":
					gameBox.Text = text3;
					break;
				case "exe":
					exeChoice = ((text3.Length > 0) ? text3 : null);
					break;
				case "mode":
				{
					if (int.TryParse(text3, out var result2))
					{
						SelectMode(result2);
					}
					break;
				}
				case "panel":
					panelBox.Checked = text3 == "1";
					break;
				case "borderless":
					borderlessBox.Checked = text3 == "1";
					break;
				case "fps60":
					fps60Box.Checked = text3 == "1";
					break;
				case "reserve":
					reserveBox.Checked = text3 == "1";
					break;
				case "smooth":
					smoothBox.Checked = text3 == "1";
					break;
				case "quality":
					qualityBox.Checked = false;
					break;
				case "faces":
					facesBox.Checked = text3 == "1";
					break;
				case "loading":
					loadingBox.Checked = text3 == "1";
					break;
				case "fights":
					fightsBox.Checked = text3 == "1";
					break;
				case "warm":
					warmBox.Checked = text3 == "1";
					break;
				case "cpu":
					cpuBox.Checked = text3 == "1";
					break;
				case "output":
				{
					if (outputBox != null && int.TryParse(text3, out var result) && result >= 0 && result <= 3)
					{
						outputBox.SelectedIndex = result;
					}
					break;
				}
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private void SaveSettings()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));
			Func<CheckBox, string> func = (CheckBox c) => (!c.Checked) ? "0" : "1";
			string[] array = new string[18]
			{
				"lang=" + (Pt ? "pt" : (Fr ? "fr" : "en")),
				"cache=" + cacheDir,
				"data=" + dataDir,
				"game=" + gameBox.Text.Trim(),
				"exe=" + (exeChoice ?? ""),
				"mode=" + modeIndex,
				"panel=" + func(panelBox),
				"borderless=" + func(borderlessBox),
				"fps60=" + func(fps60Box),
				"reserve=" + func(reserveBox),
				"smooth=" + func(smoothBox),
				"quality=" + func(qualityBox),
				"faces=" + func(facesBox),
				"loading=" + func(loadingBox),
				"fights=" + func(fightsBox),
				"warm=" + func(warmBox),
				"cpu=" + func(cpuBox),
				"output=" + ((outputBox != null) ? outputBox.SelectedIndex : 0)
			};
			HashSet<string> keys = new HashSet<string>(array.Select((string l) => l.Substring(0, l.IndexOf('=') + 1)));
			lock (settingsLock)
			{
				string[] second = (File.Exists(settingsFile) ? (from l in File.ReadAllLines(settingsFile)
					where l.Contains("=") && !keys.Contains(l.Substring(0, l.IndexOf('=') + 1))
					select l).ToArray() : new string[0]);
				WriteLinesAtomic(settingsFile, array.Concat(second).ToArray());
			}
		}
		catch (Exception)
		{
		}
	}

	private int SavesSection(Control page, int X, int y, int W)
	{
		page.Controls.Add(new Heading(L("Game saves", "Saves do jogo", "Sauvegardes du jeu"))
		{
			Location = new Point(X, y),
			Width = W
		});
		y += 34;
		RoundCard card = new RoundCard
		{
			Location = new Point(X, y),
			Width = W
		};
		int num = 16;
		Label label = Kit.Text(L("Share your progress or play a friend's: export makes a zip on your Desktop; import puts a friend's zip in place (your own saves are kept in a backup first).", "Partilha o teu progresso ou joga o de um amigo: exportar cria um zip no Ambiente de trabalho; importar poe o zip de um amigo no lugar (os teus saves ficam primeiro numa copia de seguranca).", "Partage ta progression ou joue celle d'un ami : exporter crée un zip sur ton Bureau ; importer met le zip d'un ami à la place (tes sauvegardes sont d'abord gardées dans une copie de sécurité)."), Theme.Ui(9.25f), Theme.Soft, Theme.Card, 20, num, W - 40);
		card.Controls.Add(label);
		num += label.Height + 10;
		StatusMark mark = new StatusMark
		{
			Location = new Point(20, num + 2),
			Size = new Size(20, 20),
			Kind = 3
		};
		Label status = new Label
		{
			Location = new Point(50, num),
			Size = new Size(W - 70, 24),
			Font = Theme.UiBold(9.25f),
			BackColor = Theme.Card,
			ForeColor = Theme.Dim,
			UseMnemonic = false,
			AutoEllipsis = true,
			TextAlign = ContentAlignment.MiddleLeft
		};
		card.Controls.Add(mark);
		card.Controls.Add(status);
		Action showSaves = () =>
		{
			DateTime dateTime = DateTime.MinValue;
			try
			{
				if (HasFiles(SavesDir))
				{
					dateTime = (from f in Directory.EnumerateFiles(SavesDir, "*", System.IO.SearchOption.AllDirectories)
						select File.GetLastWriteTime(f)).Max();
				}
			}
			catch (Exception)
			{
			}
			if (dateTime == DateTime.MinValue)
			{
				mark.Set(3, "");
				status.ForeColor = Theme.Dim;
				status.Text = L("No save yet: play until the game saves once.", "Ainda nao ha save: joga ate o jogo gravar uma vez.", "Pas encore de sauvegarde : joue jusqu'à ce que le jeu sauvegarde une fois.");
			}
			else
			{
				mark.Set(1, "");
				status.ForeColor = Theme.Text;
				status.Text = string.Format(L("Your save: last written {0}", "O teu save: ultima gravacao {0}", "Ta sauvegarde : dernière écriture {0}"), dateTime.ToString("g"));
			}
		};
		showSaves();
		card.VisibleChanged += (object s, EventArgs e) =>
		{
			if (card.Visible)
			{
				showSaves();
			}
		};
		num += 38;
		Pill pill = new Pill();
		pill.Text = L("Export my save", "Exportar o meu save", "Exporter ma sauvegarde");
		pill.Size = new Size(200, 38);
		Pill pill2 = pill;
		pill2.Click += (object s, EventArgs e) =>
		{
			ExportSave();
		};
		Pill pill3 = new Pill();
		pill3.Text = L("Import a save...", "Importar um save...", "Importer une sauvegarde...");
		pill3.Size = new Size(200, 38);
		Pill pill4 = pill3;
		pill4.Click += (object s, EventArgs e) =>
		{
			ImportSave();
		};
		Pill pill5 = new Pill();
		pill5.Text = L("Open the saves folder", "Abrir a pasta dos saves", "Ouvrir le dossier");
		pill5.Size = new Size(200, 38);
		Pill pill6 = pill5;
		pill6.Click += (object s, EventArgs e) =>
		{
			Directory.CreateDirectory(SavesDir);
			try
			{
				Process.Start("explorer.exe", "\"" + SavesDir + "\"");
			}
			catch (Exception)
			{
			}
		};
		Pill pill7 = new Pill();
		pill7.Text = L("Share in the gallery", "Partilhar na galeria", "Partager dans la galerie");
		pill7.Size = new Size(200, 38);
		Pill pill8 = pill7;
		pill8.Click += (object s, EventArgs e) =>
		{
			ShareSave();
		};
		Pill pill9 = new Pill();
		pill9.Text = L("Open the save gallery", "Abrir a galeria de saves", "Ouvrir la galerie");
		pill9.Size = new Size(200, 38);
		Pill pill10 = pill9;
		pill10.Click += (object s, EventArgs e) =>
		{
			OpenUrl("https://vi-hardware.edupodept.workers.dev/saves");
		};
		Pill pill11 = new Pill();
		pill11.Text = L("Restore a backup...", "Restaurar uma copia...", "Restaurer une copie...");
		pill11.Size = new Size(200, 38);
		Pill pill12 = pill11;
		pill12.Click += (object s, EventArgs e) =>
		{
			RestoreBackup();
		};
		num = Kit.Row(card, 20, num, W - 40, pill2, pill4, pill6, pill8, pill10, pill12);
		card.Height = num + 18;
		page.Controls.Add(card);
		return y + card.Height + 16;
	}

	private void ShareSave()
	{
		if (!HasFiles(SavesDir))
		{
			MessageBox.Show(this, L("There is no save yet: play until the game saves once.", "Ainda nao ha save: joga ate o jogo gravar uma vez.", "Pas encore de sauvegarde : joue jusqu'à ce que le jeu sauvegarde une fois."), "Julia's Launcher");
			return;
		}
		Form f = Dialog(L("Share in the gallery", "Partilhar na galeria", "Partager dans la galerie"), 580, 400);
		string stringToEscape;
		string stringToEscape2;
		try
		{
			int num = 20;
			Label label = Kit.Text(L("Your save goes to the public gallery once edupode has approved it. Everyone can download it.", "O teu save vai para a galeria publica depois de o edupode o aprovar. Qualquer pessoa o pode descarregar.", "Ta sauvegarde arrive dans la galerie publique une fois qu'edupode l'a validée. Tout le monde pourra la télécharger."), Theme.Ui(9.5f), Theme.Soft, Theme.Back, 24, num, 532);
			f.Controls.Add(label);
			num += label.Height + 14;
			Label label2 = Kit.Text(L("Title (where you are in the story)", "Titulo (onde estas na historia)", "Titre (où tu en es dans l'histoire)"), Theme.UiBold(9.5f), Theme.Text, Theme.Back, 24, num, 532);
			f.Controls.Add(label2);
			num += label2.Height + 4;
			TextBox t = new TextBox
			{
				MaxLength = 80,
				ForeColor = Theme.Text,
				Font = Theme.Ui(10f)
			};
			Kit.Field(f, t, 24, num, 532);
			num += 52;
			Label label3 = Kit.Text(L("Note (optional)", "Nota (opcional)", "Note (facultatif)"), Theme.UiBold(9.5f), Theme.Text, Theme.Back, 24, num, 532);
			f.Controls.Add(label3);
			num += label3.Height + 4;
			TextBox textBox = new TextBox();
			textBox.MaxLength = 300;
			textBox.Multiline = true;
			textBox.ForeColor = Theme.Text;
			textBox.Font = Theme.Ui(10f);
			TextBox textBox2 = textBox;
			Kit.Field(f, textBox2, 24, num, 532, 92);
			num += 104;
			Label hint = Kit.Text(L("The title needs at least 3 letters.", "O titulo precisa de pelo menos 3 letras.", "Le titre doit avoir au moins 3 lettres."), Theme.Ui(9f), Theme.Dim, Theme.Back, 24, num, 532);
			f.Controls.Add(hint);
			num += hint.Height + 12;
			Pill pill = new Pill();
			pill.Main = true;
			pill.Text = L("Share", "Partilhar", "Partager");
			pill.Size = new Size(180, 40);
			Pill pill2 = pill;
			Pill pill3 = new Pill();
			pill3.Text = L("Cancel", "Cancelar", "Annuler");
			pill3.Size = new Size(150, 40);
			Pill pill4 = pill3;
			pill2.Click += (object s, EventArgs e) =>
			{
				if (t.Text.Trim().Length >= 3)
				{
					f.DialogResult = DialogResult.OK;
					f.Close();
				}
				else
				{
					hint.ForeColor = Theme.Warn;
					t.Focus();
				}
			};
			pill4.Click += (object s, EventArgs e) =>
			{
				f.Close();
			};
			num = Kit.Row(f, 24, num, 532, pill2, pill4);
			f.ClientSize = new Size(580, num + 22);
			if (f.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			stringToEscape = t.Text.Trim();
			stringToEscape2 = textBox2.Text.Trim();
		}
		finally
		{
			if (f != null)
			{
				((IDisposable)f).Dispose();
			}
		}
		string text = Path.Combine(Path.GetTempPath(), "vi-save-share.zip");
		try
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
			ZipFile.CreateFromDirectory(SavesDir, text, CompressionLevel.Optimal, includeBaseDirectory: false);
			byte[] array = File.ReadAllBytes(text);
			if (array.Length > 1048576)
			{
				MessageBox.Show(this, L("This save is too big for the gallery (over 1 MB).", "Este save e grande demais para a galeria (mais de 1 MB).", "Cette sauvegarde est trop grosse pour la galerie (plus de 1 Mo)."), "Julia's Launcher");
				return;
			}
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create("https://vi-hardware.edupodept.workers.dev/v1/saves");
			httpWebRequest.Method = "POST";
			httpWebRequest.ContentType = "application/zip";
			httpWebRequest.UserAgent = "Julias-Launcher";
			httpWebRequest.Timeout = 60000;
			httpWebRequest.Headers["X-VI-Id"] = (hw ?? ReadHardware()).Id;
			httpWebRequest.Headers["X-VI-Title"] = Uri.EscapeDataString(stringToEscape);
			httpWebRequest.Headers["X-VI-Note"] = Uri.EscapeDataString(stringToEscape2);
			httpWebRequest.ContentLength = array.Length;
			using (Stream stream = httpWebRequest.GetRequestStream())
			{
				stream.Write(array, 0, array.Length);
			}
			using ((HttpWebResponse)httpWebRequest.GetResponse())
			{
			}
			Log(L("Save shared: it shows in the gallery once approved.", "Save partilhado: aparece na galeria depois de aprovado.", "Sauvegarde partagée : elle apparaîtra dans la galerie une fois validée."));
			MessageBox.Show(this, L("Save shared: it shows in the gallery once approved.", "Save partilhado: aparece na galeria depois de aprovado.", "Sauvegarde partagée : elle apparaîtra dans la galerie une fois validée."), "Julia's Launcher");
		}
		catch (WebException ex)
		{
			HttpWebResponse httpWebResponse = ex.Response as HttpWebResponse;
			MessageBox.Show(this, (httpWebResponse != null && httpWebResponse.StatusCode == (HttpStatusCode)429) ? L("Too many saves shared this hour: try again later.", "Demasiados saves partilhados nesta hora: tenta mais tarde.", "Trop de sauvegardes partagées cette heure-ci : réessaie plus tard.") : (L("The save could not be shared: ", "Nao deu para partilhar o save: ", "Impossible de partager la sauvegarde : ") + ex.Message), "Julia's Launcher");
		}
		catch (Exception ex2)
		{
			MessageBox.Show(this, L("The save could not be shared: ", "Nao deu para partilhar o save: ", "Impossible de partager la sauvegarde : ") + ex2.Message, "Julia's Launcher");
		}
		finally
		{
			try
			{
				File.Delete(text);
			}
			catch (Exception)
			{
			}
		}
	}

	private static bool HasFiles(string dir)
	{
		if (Directory.Exists(dir))
		{
			return Directory.EnumerateFiles(dir, "*", System.IO.SearchOption.AllDirectories).Any();
		}
		return false;
	}

	private static string NotSaveBytes(string name, byte[] head, long length)
	{
		if (length > (name.EndsWith("manifest") ? 65536 : 33554432))
		{
			return "too large";
		}
		if (head.Length >= 2 && head[0] == 77 && head[1] == 90)
		{
			return "a Windows program";
		}
		if (head.Length >= 2 && head[0] == 80 && head[1] == 75)
		{
			return "an archive";
		}
		if (head.Length >= 4 && head[0] == 127 && head[1] == 69 && head[2] == 76 && head[3] == 70)
		{
			return "a program";
		}
		if (head.Length >= 4 && head[0] == 76 && head[1] == 0 && head[2] == 0 && head[3] == 0)
		{
			return "a shortcut";
		}
		if (head.Length >= 2 && head[0] == 35 && head[1] == 33)
		{
			return "a script";
		}
		string text = Encoding.ASCII.GetString(head).TrimStart().ToLowerInvariant();
		string[] array = new string[7] { "@echo", "<script", "<html", "powershell", "function ", "set ws", "createobject(" };
		foreach (string value in array)
		{
			if (text.StartsWith(value))
			{
				return "a script";
			}
		}
		return null;
	}

	private static byte[] Head(Stream s, int n)
	{
		byte[] array = new byte[n];
		int i;
		int num;
		for (i = 0; i < n; i += num)
		{
			if ((num = s.Read(array, i, n - i)) <= 0)
			{
				break;
			}
		}
		Array.Resize(ref array, i);
		return array;
	}

	private static string Sha256Of(Stream s)
	{
		using SHA256 sHA = SHA256.Create();
		return string.Concat((from x in sHA.ComputeHash(s)
			select x.ToString("x2")).ToArray());
	}

	private void ExportSave()
	{
		if (!HasFiles(SavesDir))
		{
			MessageBox.Show(this, L("There is no save yet: play until the game saves once.", "Ainda nao ha save: joga ate o jogo gravar uma vez.", "Pas encore de sauvegarde : joue jusqu'à ce que le jeu sauvegarde une fois."), "Julia's Launcher");
			return;
		}
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VI-save-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip");
		try
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
			StringBuilder stringBuilder = new StringBuilder();
			int num = 0;
			using (ZipArchive zipArchive = ZipFile.Open(text, ZipArchiveMode.Create))
			{
				foreach (string item in Directory.EnumerateFiles(SavesDir, "*", System.IO.SearchOption.AllDirectories))
				{
					string text2 = item.Substring(SavesDir.Length).TrimStart('\\').Replace('\\', '/');
					if (SavePath.IsMatch(text2))
					{
						zipArchive.CreateEntryFromFile(item, text2, CompressionLevel.Optimal);
						using (FileStream s = File.OpenRead(item))
						{
							stringBuilder.Append(Sha256Of(s)).Append("  ").Append(text2)
								.Append('\n');
						}
						num++;
					}
				}
				ZipArchiveEntry zipArchiveEntry = zipArchive.CreateEntry("vi-save.sha256");
				using StreamWriter streamWriter = new StreamWriter(zipArchiveEntry.Open());
				streamWriter.Write(stringBuilder.ToString());
			}
			if (num == 0)
			{
				File.Delete(text);
				MessageBox.Show(this, L("There is no save yet: play until the game saves once.", "Ainda nao ha save: joga ate o jogo gravar uma vez.", "Pas encore de sauvegarde : joue jusqu'à ce que le jeu sauvegarde une fois."), "Julia's Launcher");
				return;
			}
			Log(L("Save exported: ", "Save exportado: ", "Sauvegarde exportée : ") + text);
			try
			{
				Process.Start("explorer.exe", "/select,\"" + text + "\"");
			}
			catch (Exception)
			{
			}
		}
		catch (Exception ex2)
		{
			MessageBox.Show(this, L("The save could not be exported: ", "Nao deu para exportar o save: ", "Impossible d'exporter la sauvegarde : ") + ex2.Message, "Julia's Launcher");
		}
	}

	private void ImportSave()
	{
		if (State() != GameState.None)
		{
			MessageBox.Show(this, L("Close the game first.", "Fecha o jogo primeiro.", "Ferme d'abord le jeu."), "Julia's Launcher");
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog();
		openFileDialog.Filter = "Zip (*.zip)|*.zip";
		openFileDialog.Title = L("A save exported by the launcher", "Um save exportado pelo launcher", "Une sauvegarde exportée par le launcher");
		string fileName;
		using (OpenFileDialog openFileDialog2 = openFileDialog)
		{
			if (openFileDialog2.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			fileName = openFileDialog2.FileName;
		}
		Directory.CreateDirectory(SaveBackups);
		string text = Path.Combine(SaveBackups, "incoming-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
		try
		{
			string text2 = null;
			using (ZipArchive zipArchive = ZipFile.OpenRead(fileName))
			{
				List<ZipArchiveEntry> list = zipArchive.Entries.Where((ZipArchiveEntry e) => !e.FullName.EndsWith("/")).ToList();
				if (list.Count == 0 || list.Count > 401)
				{
					text2 = L("it holds no save, or far too many files", "nao tem save, ou tem ficheiros a mais", "il ne contient pas de sauvegarde, ou beaucoup trop de fichiers");
				}
				else if (list.Sum((ZipArchiveEntry e) => e.Length) > 100663296)
				{
					text2 = L("it is far larger than a save", "e muito maior do que um save", "il est bien plus gros qu'une sauvegarde");
				}
				Dictionary<string, string> dictionary = null;
				ZipArchiveEntry zipArchiveEntry = list.FirstOrDefault((ZipArchiveEntry e) => e.FullName == "vi-save.sha256");
				if (text2 == null && zipArchiveEntry != null)
				{
					dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
					using StreamReader streamReader = new StreamReader(zipArchiveEntry.Open());
					string[] array = streamReader.ReadToEnd().Split('\n');
					foreach (string text3 in array)
					{
						string[] array2 = text3.Trim().Split(new string[1] { "  " }, 2, StringSplitOptions.None);
						if (array2.Length == 2)
						{
							dictionary[array2[1]] = array2[0];
						}
					}
				}
				string value = Path.GetFullPath(text) + Path.DirectorySeparatorChar;
				foreach (ZipArchiveEntry item in list)
				{
					if (text2 != null)
					{
						break;
					}
					if (item == zipArchiveEntry)
					{
						continue;
					}
					string text4 = item.FullName.Replace('\\', '/');
					if (!SavePath.IsMatch(text4))
					{
						text2 = string.Format(L("\"{0}\" is not a save file", "\"{0}\" nao e um ficheiro de save", "\"{0}\" n'est pas un fichier de sauvegarde"), text4);
						break;
					}
					string text5;
					using (Stream s = item.Open())
					{
						text5 = NotSaveBytes(text4, Head(s, 64), item.Length);
					}
					if (text5 != null)
					{
						text2 = string.Format(L("\"{0}\" is {1}", "\"{0}\" e {1}", "\"{0}\" est {1}"), text4, text5);
						break;
					}
					string fullPath = Path.GetFullPath(Path.Combine(text, text4.Replace('/', Path.DirectorySeparatorChar)));
					if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase))
					{
						text2 = text4;
						break;
					}
					Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
					item.ExtractToFile(fullPath, overwrite: false);
					if (dictionary != null)
					{
						string text6;
						using (FileStream s2 = File.OpenRead(fullPath))
						{
							text6 = Sha256Of(s2);
						}
						if (!dictionary.TryGetValue(text4, out var value2) || value2 != text6)
						{
							text2 = string.Format(L("\"{0}\" was changed after it was exported", "\"{0}\" foi alterado depois de exportado", "\"{0}\" a été modifié après l'export"), text4);
							break;
						}
					}
				}
				if (text2 == null && !Directory.Exists(text))
				{
					text2 = L("it holds no save", "nao tem save", "il ne contient pas de sauvegarde");
				}
			}
			if (text2 != null)
			{
				try
				{
					if (Directory.Exists(text))
					{
						Directory.Delete(text, recursive: true);
					}
				}
				catch (Exception)
				{
				}
				Log(L("Save refused: ", "Save recusado: ", "Sauvegarde refusée : ") + text2);
				MessageBox.Show(this, L("This zip was refused, nothing was changed: ", "Este zip foi recusado, nada foi mudado: ", "Ce zip a été refusé, rien n'a changé : ") + text2 + ".\r\n\r\n" + L("Only saves exported by Julia's Launcher are taken.", "So sao aceites saves exportados pelo Julia's Launcher.", "Seules les sauvegardes exportées par Julia's Launcher sont acceptées."), "Julia's Launcher", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return;
			}
			string text7 = L("Replace your saves with this one? Yours are kept in a backup first (", "Substituir os teus saves por este? Os teus ficam primeiro numa copia de seguranca (", "Remplacer tes sauvegardes par celle-ci ? Les tiennes sont d'abord gardées dans une copie de sécurité (") + SaveBackups + ").";
			if (MessageBox.Show(this, text7, "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
			{
				Directory.Delete(text, recursive: true);
				return;
			}
			if (HasFiles(SavesDir))
			{
				string text8 = Path.Combine(SaveBackups, "saves-before-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
				ZipFile.CreateFromDirectory(SavesDir, text8, CompressionLevel.Optimal, includeBaseDirectory: false);
				Directory.Delete(SavesDir, recursive: true);
				Log(L("Your saves were kept in ", "Os teus saves ficaram em ", "Tes sauvegardes sont gardées dans ") + text8);
			}
			Directory.CreateDirectory(Path.GetDirectoryName(SavesDir));
			Directory.Move(text, SavesDir);
			Log(L("Save imported. Press PLAY and load it from the game's menu.", "Save importado. Carrega em JOGAR e abre-o no menu do jogo.", "Sauvegarde importée. Appuie sur PLAY et charge-la depuis le menu du jeu."));
			MessageBox.Show(this, L("Save imported. Press PLAY and load it from the game's menu.", "Save importado. Carrega em JOGAR e abre-o no menu do jogo.", "Sauvegarde importée. Appuie sur PLAY et charge-la depuis le menu du jeu."), "Julia's Launcher");
		}
		catch (Exception ex2)
		{
			try
			{
				if (Directory.Exists(text))
				{
					Directory.Delete(text, recursive: true);
				}
			}
			catch (Exception)
			{
			}
			MessageBox.Show(this, L("The save could not be imported: ", "Nao deu para importar o save: ", "Impossible d'importer la sauvegarde : ") + ex2.Message, "Julia's Launcher");
		}
	}

	private void BackupSavesBeforePlay()
	{
		try
		{
			if (!HasFiles(SavesDir))
			{
				return;
			}
			Directory.CreateDirectory(SaveBackups);
			FileInfo fileInfo = (from f in new DirectoryInfo(SaveBackups).EnumerateFiles("auto-*.zip")
				orderby f.LastWriteTimeUtc descending
				select f).FirstOrDefault();
			DateTime dateTime = Directory.EnumerateFiles(SavesDir, "*", System.IO.SearchOption.AllDirectories).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue)
				.Max();
			if (fileInfo != null && fileInfo.LastWriteTimeUtc >= dateTime)
			{
				return;
			}
			string destinationArchiveFileName = Path.Combine(SaveBackups, "auto-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
			ZipFile.CreateFromDirectory(SavesDir, destinationArchiveFileName, CompressionLevel.Optimal, includeBaseDirectory: false);
			foreach (FileInfo item in (from f in new DirectoryInfo(SaveBackups).EnumerateFiles("auto-*.zip")
				orderby f.LastWriteTimeUtc descending
				select f).Skip(15))
			{
				try
				{
					item.Delete();
				}
				catch (Exception)
				{
				}
			}
		}
		catch (Exception ex2)
		{
			Log(L("Save backup: ", "Copia dos saves: ", "Copie des sauvegardes : ") + ex2.Message);
		}
	}

	private void RestoreBackup()
	{
		if (State() != GameState.None)
		{
			MessageBox.Show(this, L("Close the game first.", "Fecha o jogo primeiro.", "Ferme d'abord le jeu."), "Julia's Launcher");
			return;
		}
		List<FileInfo> list = (Directory.Exists(SaveBackups) ? (from fileInfo in new DirectoryInfo(SaveBackups).EnumerateFiles("*.zip")
			orderby fileInfo.LastWriteTime descending
			select fileInfo).ToList() : new List<FileInfo>());
		if (list.Count == 0)
		{
			MessageBox.Show(this, L("There is no backup yet: one is made before every game.", "Ainda nao ha copias: e feita uma antes de cada jogo.", "Pas encore de copie : une est faite avant chaque partie."), "Julia's Launcher");
			return;
		}
		Form f = Dialog(L("Restore a backup", "Restaurar uma copia", "Restaurer une copie"), 580, 520);
		try
		{
			Label label = Kit.Text(L("Your saves as they were before each game (newest first). The current ones are kept in a backup too.", "Os teus saves como estavam antes de cada jogo (os mais recentes primeiro). Os atuais tambem ficam numa copia.", "Tes sauvegardes telles qu'avant chaque partie (les plus récentes d'abord). Les actuelles sont aussi gardées."), Theme.Ui(9.5f), Theme.Soft, Theme.Back, 24, 20, 532);
			f.Controls.Add(label);
			int num = 20 + label.Height + 12;
			RoundCard roundCard = new RoundCard();
			roundCard.Location = new Point(24, num);
			roundCard.Size = new Size(532, 330);
			RoundCard roundCard2 = roundCard;
			ThemedList themedList = new ThemedList();
			themedList.Location = new Point(8, 8);
			themedList.Size = new Size(516, 314);
			ThemedList themedList2 = themedList;
			roundCard2.Controls.Add(themedList2);
			f.Controls.Add(roundCard2);
			foreach (FileInfo item in list)
			{
				themedList2.Items.Add(item.LastWriteTime.ToString("g") + "   " + (item.Name.StartsWith("auto-") ? L("before a game", "antes de um jogo", "avant une partie") : L("before an import", "antes de importar", "avant un import")) + "   " + Math.Max(1L, item.Length / 1024) + " KB");
			}
			themedList2.SelectedIndex = 0;
			Pill pill = new Pill();
			pill.Main = true;
			pill.Text = L("Restore", "Restaurar", "Restaurer");
			pill.Size = new Size(180, 40);
			Pill pill2 = pill;
			Pill pill3 = new Pill();
			pill3.Text = L("Cancel", "Cancelar", "Annuler");
			pill3.Size = new Size(150, 40);
			Pill pill4 = pill3;
			pill2.Click += (object s, EventArgs e) =>
			{
				f.DialogResult = DialogResult.OK;
				f.Close();
			};
			pill4.Click += (object s, EventArgs e) =>
			{
				f.Close();
			};
			int num2 = Kit.Row(f, 24, roundCard2.Bottom + 16, 532, pill2, pill4);
			f.ClientSize = new Size(580, num2 + 22);
			if (f.ShowDialog(this) != DialogResult.OK || themedList2.SelectedIndex < 0)
			{
				return;
			}
			string fullName = list[themedList2.SelectedIndex].FullName;
			try
			{
				if (HasFiles(SavesDir))
				{
					string destinationArchiveFileName = Path.Combine(SaveBackups, "saves-before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
					ZipFile.CreateFromDirectory(SavesDir, destinationArchiveFileName, CompressionLevel.Optimal, includeBaseDirectory: false);
					Directory.Delete(SavesDir, recursive: true);
				}
				Directory.CreateDirectory(SavesDir);
				ZipFile.ExtractToDirectory(fullName, SavesDir);
				Log(L("Saves restored from ", "Saves restaurados de ", "Sauvegardes restaurées depuis ") + Path.GetFileName(fullName));
				MessageBox.Show(this, L("Saves restored. Press PLAY and load from the game's menu.", "Saves restaurados. Carrega em JOGAR e abre-os no menu do jogo.", "Sauvegardes restaurées. Appuie sur PLAY et charge-les depuis le menu du jeu."), "Julia's Launcher");
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, L("The backup could not be restored: ", "Nao deu para restaurar a copia: ", "Impossible de restaurer la copie : ") + ex.Message, "Julia's Launcher");
			}
		}
		finally
		{
			if (f != null)
			{
				((IDisposable)f).Dispose();
			}
		}
	}

	[DllImport("kernel32.dll")]
	private static extern bool GetFirmwareType(out uint type);

	private static bool Uefi()
	{
		try
		{
			uint type;
			return GetFirmwareType(out type) && type == 2;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private Panel SetupPage()
	{
		Panel panel = Page();
		string sub = L("Each step checks your PC. Press its button, or 'Do everything for me' to run the missing ones in order. Windows asks before anything that needs administrator rights.", "Cada passo verifica o teu PC. Carrega no botao dele, ou em 'Fazer tudo por mim' para correr os que faltam por ordem. O Windows pergunta antes de tudo o que precisa de administrador.", "Chaque étape vérifie ton PC. Appuie sur son bouton, ou sur 'Tout faire pour moi' pour lancer celles qui manquent dans l'ordre. Windows demande avant tout ce qui exige les droits administrateur.");
		string text = L("Do everything for me", "Fazer tudo por mim", "Tout faire pour moi") + "  →";
		everyW = Kit.PillW(text, 11f, 230);
		setupHead = Head(L("Setup", "Preparar", "Préparer"), L("Setup", "Preparar", "Préparer"), L("done for you.", "feito por ti.", "fait pour toi."), sub);
		setupHead.RightLabel = L("Progress", "Progresso", "Avancement");
		setupHead.RightValue = L("Checking your PC...", "A verificar o teu PC...", "Vérification de ton PC...");
		everything = new Pill
		{
			Main = true,
			Text = text,
			Font = new Font("Segoe UI Semibold", 11f),
			Size = new Size(everyW, 46)
		};
		everything.Click += (object s, EventArgs e) =>
		{
			DoEverything();
		};
		setupHead.Controls.Add(everything);
		Action placeEvery = () =>
		{
			everything.Location = new Point(Math.Max(40, setupHead.Width - 40 - everyW), 84);
		};
		setupHead.Resize += (object s, EventArgs e) =>
		{
			placeEvery();
		};
		placeEvery();
		panel.Controls.Add(setupHead);
		setupAnchor = setupHead;
		int num = setupHead.Height + 8;
		setupBar = new LoadBar
		{
			Location = new Point(40, num),
			Size = new Size(900, 30),
			Visible = false
		};
		panel.Controls.Add(setupBar);
		num += 44;
		DefineSteps();
		Font f = Theme.Ui(9f);
		Font f2 = Theme.UiBold(10.5f);
		int val = 110;
		foreach (Step step in steps)
		{
			val = Math.Max(val, Kit.PillW(step.FixText, 9f, 110));
		}
		val = Math.Min(val, 190);
		int num2 = 96;
		int num3 = 844 - val - 28;
		int num4 = 0;
		using (Font f3 = Theme.UiBold(8.5f))
		{
			string[] array = new string[6]
			{
				L("Not needed", "Nao preciso", "Inutile"),
				L("Checking...", "A verificar...", "Vérification..."),
				L("Not done", "Nao feito", "Pas fait"),
				L("Running", "A correr", "En cours"),
				L("To do", "Por fazer", "À faire"),
				L("Done", "Feito", "Fait")
			};
			foreach (string text2 in array)
			{
				num4 = Math.Max(num4, Kit.TextW(text2, f3));
			}
		}
		int num6 = num;
		int num7 = 0;
		int num8 = num;
		foreach (Step step2 in steps)
		{
			num7++;
			int val2 = Kit.TextW(step2.Title, f2) + 6;
			int num9 = num4 + 14;
			int num10 = Math.Min(val2, num3 - num9 - 6);
			int val3 = Kit.TextH(step2.Title, f2, num10);
			int num11 = Kit.TextH(step2.Why, f, num3);
			int num12 = Math.Max(62, 2 + Math.Max(val3, 22) + 2 + num11 + 16);
			step2.Badge = new StatusMark
			{
				Node = true,
				Number = num7.ToString("00"),
				Location = new Point(40, num),
				Size = new Size(30, 30),
				Kind = 4
			};
			step2.Text = Kit.Text(step2.Title, f2, Theme.Text, Theme.Back, num2, num + 2, num10);
			step2.Chip = new StatusMark
			{
				Flat = true,
				Round = false,
				Kind = 4,
				Size = new Size(num9, 22),
				Location = new Point(num2 + num10 + 6, num + 3),
				Text = L("Checking...", "A verificar...", "Vérification...")
			};
			step2.Detail = Kit.Text(step2.Why, f, Theme.Muted, Theme.Back, num2, num + 2 + Math.Max(val3, 22) + 2, num3);
			step2.Button = new Pill
			{
				Text = step2.FixText,
				Font = Theme.Ui(9f),
				Size = new Size(val, 30)
			};
			step2.Button.Location = new Point(940 - val, num);
			Step me = step2;
			step2.Button.Click += (object s, EventArgs e) =>
			{
				RunSteps(new Step[1] { me });
			};
			panel.Controls.Add(step2.Badge);
			panel.Controls.Add(step2.Text);
			panel.Controls.Add(step2.Chip);
			panel.Controls.Add(step2.Detail);
			panel.Controls.Add(step2.Button);
			num8 = num;
			step2.NodeY = num;
			num += num12;
		}
		timelineFirstY = num6;
		tline = new TimelineLine
		{
			Location = new Point(54, num6 + 15),
			Size = new Size(3, Math.Max(1, num8 - num6))
		};
		TimelineLine timelineLine = tline;
		panel.Controls.Add(timelineLine);
		timelineLine.SendToBack();
		ShowSummary();
		panel.Controls.Add(new Label
		{
			Location = new Point(40, num + 8),
			Size = new Size(10, 14),
			BackColor = Theme.Back
		});
		return panel;
	}

	private void ShowSummary()
	{
		if (setupHead == null)
		{
			return;
		}
		int num = Enumerable.Count(steps, (Step s) => s.State != -1 && s.State != -2);
		int num2 = Enumerable.Count(steps, (Step s) => s.State == 1);
		ref string rightValue = ref setupHead.RightValue;
		string text;
		if (num != 0)
		{
			text = ((num2 == num) ? L("Everything is ready.", "Esta tudo pronto.", "Tout est prêt.") : string.Format(L("{0} of {1} ready", "{0} de {1} prontos", "{0} sur {1} prêts"), num2, num));
		}
		else
		{
			text = L("Checking your PC...", "A verificar o teu PC...", "Vérification de ton PC...");
		}
		rightValue = text;
		setupHead.RightColor = ((num > 0 && num2 == num) ? Theme.Good : Theme.Cyan);
		if (tline != null)
		{
			int num3 = -1;
			for (int num4 = 0; num4 < steps.Count; num4++)
			{
				int num5 = steps[num4].State;
				if (num5 != 1 && num5 != -1)
				{
					break;
				}
				num3 = num4;
			}
			tline.SetFill((num3 >= 0) ? (steps[num3].NodeY - timelineFirstY) : 0);
		}
		setupHead.Invalidate();
	}

	private void DefineSteps()
	{
		steps.Clear();
		steps.Add(new Step
		{
			Title = L("Rust (builds VIctory)", "Rust (compila o VIctory)"),
			Why = L("The compiler VIctory is written for. Installed with winget (rustup).", "O compilador do VIctory. Instalado com o winget (rustup)."),
			FixText = L("Install", "Instalar"),
			Check = () => (!NothingToBuild) ? (HasCargo() ? 1 : 0) : (-1),
			Fix = () => Winget("Rustlang.Rustup", null)
		});
		steps.Add(new Step
		{
			Title = L("Visual Studio Build Tools (C++)", "Visual Studio Build Tools (C++)"),
			Why = L("The C++ linker and Windows SDK Rust needs. A large download (2-6 GB); Windows asks for permission.", "O linker de C++ e o Windows SDK de que o Rust precisa. Download grande (2-6 GB); o Windows pede autorizacao."),
			FixText = L("Install", "Instalar"),
			Check = () => (!NothingToBuild) ? (HasCppTools() ? 1 : 0) : (-1),
			Fix = () => Winget("Microsoft.VisualStudio.2022.BuildTools", "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended")
		});
		steps.Add(new Step
		{
			Title = L("Build VIctory", "Compilar o VIctory"),
			Why = L("Compiles the emulator from this folder (several minutes, once; again after an update).", "Compila o emulador a partir desta pasta (alguns minutos, uma vez; de novo depois de atualizar)."),
			FixText = L("Build", "Compilar"),
			Check = () =>
			{
				if (!UseAmdEngine)
				{
					if (VictoryExe == null)
					{
						return 0;
					}
					return 1;
				}
				return (OwnVictoryExe != null) ? 1 : (-1);
			},
			Fix = BuildBlocking
		});
		steps.Add(new Step
		{
			Title = L("AMD alternative engine (Belobug)", "Motor alternativo AMD (Belobug)"),
			Why = L("Only if you chose it in Hardware & ID: VIctory-Belobug-AMD, Belobug's fork for Radeon (GPL-3.0). Downloads its source and builds it once.", "So se o escolheste em Hardware e ID: VIctory-Belobug-AMD, o fork do Belobug para Radeon (GPL-3.0). Descarrega o codigo e compila-o uma vez."),
			FixText = L("Build it", "Compilar"),
			Check = () => UseAmdEngine ? ((VictoryExe != null) ? 1 : 0) : (-1),
			Fix = BuildAmdEngine
		});
		steps.Add(new Step
		{
			Title = L("Windows Security: 'Access denied' when building", "Seguranca do Windows: 'Acesso negado' ao compilar"),
			Why = L("Only if a build from source ends with 'Access denied': Controlled folder access blocks freshly built programs. Opens Windows Security; allow victory.exe there yourself. The launcher never changes antivirus settings.", "So se uma compilacao acabar com 'Acesso negado': o Acesso controlado a pastas bloqueia programas acabados de compilar. Abre a Seguranca do Windows; permite la o victory.exe tu mesmo. O launcher nunca muda definicoes do antivirus."),
			FixText = L("Open it", "Abrir"),
			Check = () => -1,
			Auto = false,
			Fix = () =>
			{
				OpenUrl("windowsdefender://ransomwareprotection");
				return false;
			}
		});
		steps.Add(new Step
		{
			Title = L("Your game folder", "A pasta do jogo"),
			Why = L("Looks for game_scarlett_master.exe (or another game_scarlett*.exe) in Downloads, Desktop, Documents, Games folders and the root of each drive.", "Procura o game_scarlett_master.exe (ou outro game_scarlett*.exe) em Transferencias, Ambiente de trabalho, Documentos, pastas Games e na raiz de cada disco."),
			FixText = L("Find it", "Encontrar"),
			Check = () =>
			{
				string g = null;
				Invoke((Action)(() =>
				{
					g = GameExe;
				}));
				return File.Exists(g) ? 1 : 0;
			},
			Fix = FindGame
		});
		steps.Add(new Step
		{
			Title = L("Shaders prepared", "Shaders preparados"),
			Why = L("Translates the game's shaders and makes their pipelines once (about 5 minutes), so the game does not stop to do it.", "Traduz os shaders do jogo e prepara as pipelines uma vez (cerca de 5 minutos), para o jogo nao parar para isso."),
			FixText = L("Prepare", "Preparar"),
			Check = () => ShadersReady() ? 1 : 0,
			Fix = PrepareBlocking
		});
		steps.Add(new Step
		{
			Title = L("Power: plugged in, best performance", "Energia: ligado a corrente, melhor desempenho"),
			Why = L("Sets Windows' power mode to 'Best performance'. A laptop on battery runs the CPU slower: plug it in (and use the maker's performance mode, e.g. PredatorSense).", "Poe o modo de energia do Windows em 'Melhor desempenho'. Um portatil na bateria corre mais devagar: liga-o a corrente (e usa o modo de desempenho do fabricante, p. ex. PredatorSense)."),
			FixText = L("Set it", "Definir"),
			Check = () => (BestPower() && SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Offline) ? 1 : 0,
			Fix = () =>
			{
				RunWait("powercfg", "/overlaysetactive ded574b5-45a0-4f42-8737-46345c09c238", window: false);
				return BestPower();
			}
		});
		steps.Add(new Step
		{
			Title = L("Virtual memory managed by Windows", "Memoria virtual gerida pelo Windows"),
			Why = L("The game and VIctory commit 25+ GB; a fixed, small page file ends them. Windows asks for permission; it counts after a restart.", "O jogo e o VIctory reservam 25+ GB; um ficheiro de paginacao fixo e pequeno fecha-os. O Windows pede autorizacao; conta depois de reiniciar."),
			FixText = L("Set it", "Definir"),
			Check = () => PagefileOk() ? 1 : 0,
			Fix = FixPagefile
		});
		steps.Add(new Step
		{
			Title = L("Dedicated graphics card for VIctory", "Placa grafica dedicada para o VIctory"),
			Why = L("On a laptop with two graphics cards, Windows may give VIctory the slow built-in one. Sets 'High performance' for victory.exe (Windows > Graphics settings).", "Num portatil com duas placas, o Windows pode dar ao VIctory a integrada, lenta. Poe 'Alto desempenho' para o victory.exe (Windows > Definicoes de graficos)."),
			FixText = L("Set it", "Definir"),
			Check = () => (VictoryExe != null) ? ((GpuPreferenceSet() || (probeAdapter.Length > 0 && GpuPreferenceOk() && (!hasNvidia || !hasRadeon))) ? 1 : 0) : (-1),
			Fix = () =>
			{
				bool result = SetGpuPreference();
				ProbeGpu();
				return result;
			}
		});
		steps.Add(new Step
		{
			Title = "NVIDIA Smooth Motion",
			Why = L("Driver frame generation for RTX 40/50, written into the driver's profile for victory.exe (no NVIDIA App needed).", "Geracao de frames do driver para RTX 40/50, escrita no perfil do driver para o victory.exe (sem NVIDIA App)."),
			FixText = L("Turn on", "Ligar"),
			Check = () => rtxFrameGen ? ((SmoothMotion.Status() == "on") ? 1 : 0) : (-1),
			Applies = () => rtxFrameGen,
			Fix = () =>
			{
				string text = SmoothMotion.Set(on: true);
				smoothState = SmoothMotion.Status();
				if (text != null)
				{
					Log("Smooth Motion: " + text);
				}
				BeginInvoke((Action)(() =>
				{
					UpdateBanner();
					UpdateSmooth();
				}));
				return text == null;
			}
		});
		steps.Add(new Step
		{
			Title = "Resizable BAR",
			Why = L("Lets the CPU write straight into all of the video memory (faster copies). It is switched on in the PC's BIOS/UEFI: the guide shows how.", "Deixa o processador escrever diretamente em toda a VRAM (copias mais rapidas). Liga-se na BIOS/UEFI do PC: o guia mostra como."),
			FixText = L("Show me how", "Mostra-me como"),
			Check = () => rebar ? 1 : ((!RebarMissing()) ? (-1) : 0),
			Fix = () =>
			{
				BeginInvoke(new Action(ShowRebarGuide));
				return false;
			},
			Auto = false
		});
		steps.Add(new Step
		{
			Title = L("Resizable BAR in the NVIDIA driver", "Resizable BAR no driver da NVIDIA"),
			Why = L("NVIDIA's driver uses Resizable BAR only for the games on its list: this puts victory.exe on it (rBAR Enable, Enable v2, Options in its profile).", "O driver da NVIDIA so usa o Resizable BAR nos jogos da lista dele: isto poe o victory.exe nessa lista (rBAR Enable, Enable v2, Options no perfil)."),
			FixText = L("Turn on", "Ligar"),
			Check = () => (hasNvidia && rebarBios) ? (SmoothMotion.RebarSet() ? 1 : 0) : (-1),
			Applies = () => hasNvidia && rebarBios,
			Fix = () =>
			{
				string text = SmoothMotion.SetRebar(on: true);
				if (text != null && text.Contains("-137"))
				{
					text = ((Elevated(Application.ExecutablePath, "--rebar-profile") && SmoothMotion.RebarSet()) ? null : L("Windows did not give administrator rights, or the driver refused it.", "O Windows nao deu permissoes de administrador, ou o driver recusou.", "Windows n'a pas donné les droits administrateur, ou le pilote a refusé."));
				}
				if (text != null)
				{
					Log("rBAR: " + text);
				}
				else
				{
					Log(L("Resizable BAR is on in victory.exe's driver profile.", "Resizable BAR ligado no perfil do driver do victory.exe."));
				}
				return text == null;
			}
		});
	}

	private void CheckAll()
	{
		if (!IsHandleCreated || IsDisposed || setupBusy)
		{
			return;
		}
		Thread thread = new Thread(() =>
		{
			foreach (Step step in steps)
			{
				int s;
				try
				{
					s = step.Check();
				}
				catch (Exception)
				{
					s = -1;
				}
				Step me = step;
				Post(() =>
				{
					PaintStep(me, s);
				});
			}
		});
		thread.IsBackground = true;
		thread.Start();
	}

	private void PaintStep(Step st, int state)
	{
		PaintStep(st, state, failed: false);
	}

	private void PaintStep(Step st, int state, bool failed)
	{
		st.State = state;
		int num = state switch
		{
			0 => failed ? 5 : 0, 
			1 => 1, 
			_ => 3, 
		};
		string text = num switch
		{
			0 => L("To do", "Por fazer", "À faire"), 
			5 => L("Not done", "Nao feito", "Pas fait"), 
			1 => L("Done", "Feito", "Fait"), 
			_ => L("Not needed", "Nao preciso", "Inutile"), 
		};
		st.Badge.Set(num, text);
		st.Chip.Set(num, text);
		st.Button.Main = false;
		st.Button.Enabled = !setupBusy;
		st.Button.Invalidate();
		ShowSummary();
	}

	private void MarkRunning(Step st)
	{
		string text = L("Running", "A correr", "En cours");
		st.Badge.Set(2, text);
		st.Chip.Set(2, text);
	}

	private void DoEverything()
	{
		RunSteps(steps.Where((Step s) => s.Auto && s.State != 1 && (s.Applies == null || s.Applies())).ToArray());
	}

	private void RunSteps(Step[] which)
	{
		if (setupBusy || which.Length == 0)
		{
			return;
		}
		setupBusy = true;
		foreach (Step step2 in steps)
		{
			step2.Button.Enabled = false;
			step2.Button.Invalidate();
		}
		everything.Enabled = false;
		everything.Invalidate();
		Thread thread = new Thread(() =>
		{
			try
			{
				int num = 0;
				Step[] array = which;
				foreach (Step step in array)
				{
					num++;
					Step me = step;
					int now;
					try
					{
						now = step.Check();
					}
					catch (Exception)
					{
						now = -1;
					}
					if (now == 1 && which.Length > 1)
					{
						Post(() =>
						{
							PaintStep(me, 1);
						});
					}
					else
					{
						if (ClosingNow())
						{
							break;
						}
						Progress($"{step.Title} ({num}/{which.Length})...", (float)(num - 1) / (float)which.Length);
						Post(() =>
						{
							MarkRunning(me);
						});
						bool flag;
						try
						{
							flag = step.Fix();
						}
						catch (Exception ex2)
						{
							flag = false;
							Log(step.Title + ": " + ex2.Message);
						}
						try
						{
							now = step.Check();
						}
						catch (Exception)
						{
							now = -1;
						}
						bool tried = flag;
						Post(() =>
						{
							PaintStep(me, now, !tried);
						});
						LauncherForm launcherForm = this;
						string text = ((now == 1) ? "✓ " : (flag ? "• " : "✗ "));
						launcherForm.Log(text + step.Title);
						if (now == 0 && step.Fix == new Func<bool>(BuildBlocking) && which.Length > 1)
						{
							break;
						}
					}
				}
				Progress(L("Done.", "Feito."), 1f);
				Thread.Sleep(800);
			}
			catch (Exception ex4)
			{
				try
				{
					File.AppendAllText(Program.ErrorFile, string.Concat(DateTime.Now, "  setup: ", ex4, Environment.NewLine));
				}
				catch (Exception)
				{
				}
			}
			finally
			{
				setupBusy = false;
				Post(() =>
				{
					setupBar.Visible = false;
					if (loadBar != null)
					{
						loadBar.Visible = false;
					}
					everything.Enabled = true;
					everything.Invalidate();
					foreach (Step step3 in steps)
					{
						step3.Button.Enabled = true;
						step3.Button.Invalidate();
					}
					Refresh2();
				});
			}
		});
		thread.IsBackground = true;
		thread.Start();
	}

	private void Progress(string text, float part)
	{
		if (InvokeRequired)
		{
			try
			{
				if (!IsDisposed)
				{
					BeginInvoke(new Action<string, float>(Progress), text, part);
				}
				return;
			}
			catch (Exception)
			{
				return;
			}
		}
		setupProgress = part;
		if (setupBar != null && setupAnchor != null && !setupBar.Visible)
		{
			setupBar.Top = setupAnchor.Top + setupAnchor.Height + 10;
		}
		if (this.loadBar != null && loadBarAnchor != null && !this.loadBar.Visible)
		{
			this.loadBar.Top = loadBarAnchor.Top + 42;
		}
		LoadBar[] array = new LoadBar[2] { setupBar, this.loadBar };
		foreach (LoadBar loadBar in array)
		{
			if (loadBar != null)
			{
				loadBar.Visible = true;
				loadBar.Set(part, Kit.Clip(text, loadBar.Font, Math.Max(80, loadBar.Width - 70)));
			}
		}
	}

	private bool Winget(string id, string overrideArgs)
	{
		if (string.IsNullOrEmpty(RunCapture("where", "winget")))
		{
			Log(L("winget is missing (App Installer from the Microsoft Store). Opening the download page.", "Falta o winget (App Installer da Microsoft Store). A abrir a pagina de download."));
			OpenUrl(id.StartsWith("Rust") ? "https://rustup.rs" : "https://visualstudio.microsoft.com/visual-cpp-build-tools/");
			return false;
		}
		string text = "install --id " + id + " -e --silent --accept-source-agreements --accept-package-agreements";
		if (overrideArgs != null)
		{
			text = text + " --override \"" + overrideArgs + "\"";
		}
		Log("winget " + text);
		return RunWait("winget", text, window: false) == 0;
	}

	private static bool HasCppTools()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
		if (!File.Exists(text))
		{
			return false;
		}
		string text2 = RunCapture(text, "-products * -prerelease -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath");
		return !string.IsNullOrEmpty(text2.Trim());
	}

	private static string FoundCargo()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("CARGO_HOME");
		if (!string.IsNullOrEmpty(environmentVariable) && File.Exists(Path.Combine(environmentVariable, "bin", "cargo.exe")))
		{
			return Path.Combine(environmentVariable, "bin", "cargo.exe");
		}
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin", "cargo.exe");
		if (File.Exists(text))
		{
			return text;
		}
		if (!RunCapture("where", "cargo").Contains("cargo"))
		{
			return null;
		}
		return "cargo";
	}

	private static bool HasCargo()
	{
		return FoundCargo() != null;
	}

	private bool EnsureSource()
	{
		if (SourceDir != null)
		{
			return true;
		}
		string to = Path.Combine(Path.GetDirectoryName(dataRoot), "source", "cold-project-vi");
		Log(L("The launcher is not inside the source folder: downloading the source (main branch)...", "O launcher nao esta dentro da pasta do codigo: a descarregar o codigo (ramo main)..."));
		return DownloadZipInto("https://github.com/felicienradio/cold-project-vi/archive/refs/heads/main.zip", to, L("the source", "o codigo"));
	}

	private bool DownloadZipInto(string url, string to, string what)
	{
		string text = Path.Combine(Path.GetTempPath(), "vi-" + Guid.NewGuid().ToString("N") + ".zip");
		Progress(string.Format(L("Downloading {0}...", "A descarregar {0}..."), what), 0.02f);
		try
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			using (WebClient webClient = new WebClient())
			{
				webClient.Headers["User-Agent"] = "Julias-Launcher";
				webClient.DownloadFile(url, text);
			}
			string text2 = to + ".new";
			if (Directory.Exists(text2))
			{
				Directory.Delete(text2, recursive: true);
			}
			ZipFile.ExtractToDirectory(text, text2);
			string sourceDirName = Directory.GetDirectories(text2).Single();
			if (Directory.Exists(to))
			{
				Directory.Delete(to, recursive: true);
			}
			Directory.CreateDirectory(Path.GetDirectoryName(to));
			Directory.Move(sourceDirName, to);
			Directory.Delete(text2, recursive: true);
			Log(string.Format(L("Downloaded {0} into {1}", "{0} descarregado para {1}"), what, to));
			return true;
		}
		catch (Exception ex)
		{
			Log(string.Format(L("{0} could not be downloaded: {1}", "Nao deu para descarregar {0}: {1}"), what, ex.Message));
			return false;
		}
		finally
		{
			try
			{
				File.Delete(text);
			}
			catch (Exception)
			{
			}
		}
	}

	private bool BuildBlocking()
	{
		if (!EnsureSource())
		{
			return false;
		}
		return BuildIn(SourceDir, "VIctory", "build.log");
	}

	private bool BuildAmdEngine()
	{
		string amdEngineDir = AmdEngineDir;
		if (!File.Exists(Path.Combine(amdEngineDir, "Cargo.toml")))
		{
			if (!DownloadZipInto("https://github.com/Belobug/VIctory-Belobug-AMD/archive/4eb6cfdf9e61cb112f1f811344ec0dd309eddd6c.zip", amdEngineDir, L("Belobug's AMD engine", "o motor AMD do Belobug")))
			{
				return false;
			}
			string text = Path.Combine(amdEngineDir, "victory.example.toml");
			if (File.Exists(text) && !File.Exists(Path.Combine(amdEngineDir, "victory.toml")))
			{
				File.Copy(text, Path.Combine(amdEngineDir, "victory.toml"));
			}
		}
		return BuildIn(amdEngineDir, L("the AMD engine (Belobug)", "o motor AMD (Belobug)"), "build-amd.log");
	}

	private bool BuildIn(string folder, string what, string logName)
	{
		string log = Path.Combine(dataRoot, "logs", logName);
		Directory.CreateDirectory(Path.GetDirectoryName(log));
		int crates = 0;
		ProcessStartInfo processStartInfo = new ProcessStartInfo(CargoExe, "build --locked --profile diag -p victory --features diagnostics");
		processStartInfo.UseShellExecute = false;
		processStartInfo.RedirectStandardOutput = true;
		processStartInfo.RedirectStandardError = true;
		processStartInfo.CreateNoWindow = true;
		processStartInfo.WorkingDirectory = folder;
		ProcessStartInfo processStartInfo2 = processStartInfo;
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin");
		processStartInfo2.EnvironmentVariables["PATH"] = text + ";" + Environment.GetEnvironmentVariable("PATH");
		if (StorageRoot != null)
		{
			processStartInfo2.EnvironmentVariables["CARGO_TARGET_DIR"] = BuildTarget(folder);
		}
		Log(string.Format(L("Building {0}... Log: ", "A compilar {0}... Log: "), what) + log);
		StreamWriter writer = new StreamWriter(log, append: false);
		try
		{
			Process process;
			try
			{
				process = Process.Start(processStartInfo2);
			}
			catch (Exception ex)
			{
				Log(L("Could not start cargo: ", "Nao deu para iniciar o cargo: ") + ex.Message);
				return false;
			}
			DataReceivedEventHandler value = (object s, DataReceivedEventArgs e) =>
			{
				if (e.Data != null)
				{
					lock (writer)
					{
						writer.WriteLine(e.Data);
					}
					Match match = Regex.Match(e.Data, "^\\s*Compiling (\\S+)");
					if (match.Success)
					{
						crates++;
						Progress(string.Format(L("Building {2}: {0} ({1} parts)", "A compilar {2}: {0} ({1} partes)"), match.Groups[1].Value, crates, what), Math.Min(0.95f, (float)crates / 330f));
					}
				}
			};
			process.OutputDataReceived += value;
			process.ErrorDataReceived += value;
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			process.WaitForExit();
			Log((process.ExitCode == 0) ? (what + L(": built.", ": compilado.")) : (L("The build failed: see ", "A compilacao falhou: ve ") + log));
			BeginInvoke((Action)(() =>
			{
				Refresh2();
				ProbeGpu();
			}));
			if (process.ExitCode == 0)
			{
				EnsureAgility();
			}
			if (process.ExitCode != 0)
			{
				writer.Flush();
				TellErrors(log, 0L);
				BeginInvoke((Action)(() =>
				{
					MessageBox.Show(this, L("The build failed. The reason is in the Activity box and in ", "A compilacao falhou. O motivo esta na caixa Atividade e em ") + log + L("\r\n\r\nHelp > Send errors to Discord makes a package with it.", "\r\n\r\nAjuda > Enviar erros no Discord faz um pacote com isto."), "Julia's Launcher", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				}));
			}
			return process.ExitCode == 0;
		}
		finally
		{
			if (writer != null)
			{
				((IDisposable)writer).Dispose();
			}
		}
	}

	private bool PrepareBlocking()
	{
		string dir = null;
		string game = null;
		Invoke((Action)(() =>
		{
			SaveSettings();
			dir = gameBox.Text.Trim();
			game = GameExe;
		}));
		if (VictoryExe == null || !File.Exists(game))
		{
			Log(L("Build VIctory and choose the game folder first.", "Compila o VIctory e escolhe a pasta do jogo primeiro."));
			return false;
		}
		if (!RoomForCache())
		{
			return false;
		}
		bool go = true;
		Invoke((Action)(() =>
		{
			go = ClearOtherPreparations();
		}));
		if (!go)
		{
			return false;
		}
		string text = Path.Combine(dataRoot, "logs", "prepare.log");
		long mark = 0L;
		try
		{
			if (File.Exists(text))
			{
				mark = new FileInfo(text).Length;
			}
		}
		catch (Exception)
		{
		}
		Log(L("Translating the game's shaders and preparing pipelines (about 5 minutes)... Log: ", "A traduzir os shaders do jogo e a preparar pipelines (cerca de 5 minutos)... Log: ") + text);
		Progress(L("Translating the game's shaders (about 5 minutes)...", "A traduzir os shaders do jogo (cerca de 5 minutos)..."), 0.05f);
		WriteSetting("shaders", "0");
		Stage(0, 0f, L("Shaders: starting...", "Shaders: a comecar..."), live: true);
		Stage(1, 0f, L("Pipelines: after the shaders", "Pipelines: depois dos shaders"), live: true);
		int translated = -1;
		Process process = Start("shaders \"" + dir + "\"", text, (string line) =>
		{
			Match match = Regex.Match(line, "^(\\d+) shaders in the cache");
			if (match.Success)
			{
				translated = int.Parse(match.Groups[1].Value);
			}
			Match match2 = Regex.Match(line, "^progress (\\d+) of (\\d+) MB, (\\d+) shaders, (\\d+) s");
			if (match2.Success)
			{
				float num = float.Parse(match2.Groups[1].Value) / Math.Max(1f, float.Parse(match2.Groups[2].Value));
				string text2 = string.Format(L("Shaders: {0:N0} translated · {1:0}% of the game's files · {2} s", "Shaders: {0:N0} traduzidos · {1:0}% dos ficheiros do jogo · {2} s"), int.Parse(match2.Groups[3].Value), num * 100f, match2.Groups[4].Value);
				Stage(0, num, text2, live: true);
				Progress(text2, 0.05f + 0.55f * num);
			}
		});
		process.WaitForExit();
		Stage(0, 1f, (process.ExitCode == 0) ? L("Shaders: done", "Shaders: feito") : L("Shaders: failed (see the errors below)", "Shaders: falhou (ve os erros em baixo)"), live: true);
		if (process.ExitCode != 0)
		{
			Log(string.Format(L("Translating the shaders failed (code {0}).", "A traducao dos shaders falhou (codigo {0})."), process.ExitCode));
			TellErrors(text, mark);
			return false;
		}
		if (translated >= 0 && translated < 1000)
		{
			Stage(0, 1f, L("Shaders: failed (see the errors below)", "Shaders: falhou (ve os erros em baixo)"), live: false);
			Log(string.Format(L("Only {0:N0} shaders were translated (a complete dump gives about 25,000): this folder does not hold the game's files, or the dump is damaged.", "So {0:N0} shaders foram traduzidos (um dump completo da cerca de 25 000): esta pasta nao tem os ficheiros do jogo, ou o dump esta estragado.", "Seulement {0:N0} shaders ont été traduits (un dump complet en donne environ 25 000) : ce dossier ne contient pas les fichiers du jeu, ou le dump est abîmé."), translated));
			if (MissingDumpFiles(dir).Length > 0)
			{
				Log(MissingDumpText(dir));
			}
			else
			{
				Log(L("Download or extract the dump again, into a folder of its own.", "Volta a descarregar ou extrair o dump, para uma pasta so dele.", "Télécharge ou extrais le dump à nouveau, dans un dossier à lui."));
			}
			TellErrors(text, mark);
			return false;
		}
		if (ClosingNow())
		{
			return false;
		}
		Progress(L("Preparing pipelines...", "A preparar pipelines..."), 0.6f);
		Process process2 = Start("warm --used", text, (string line) =>
		{
			Match match = Regex.Match(line, "^(\\d+) of (\\d+), (\\d+) s");
			if (match.Success)
			{
				int num = int.Parse(match.Groups[1].Value);
				int num2 = int.Parse(match.Groups[2].Value);
				Progress(string.Format(L("Preparing pipelines: {0} of {1}", "A preparar pipelines: {0} de {1}"), num, num2), 0.6f + 0.4f * (float)num / (float)Math.Max(1, num2));
				Stage(1, (float)num / (float)Math.Max(1, num2), string.Format(L("Pipelines: {0:N0} of {1:N0} made · {2} s", "Pipelines: {0:N0} de {1:N0} feitas · {2} s"), num, num2, match.Groups[3].Value), live: true);
			}
			Match match2 = Regex.Match(line, "; 0 to make");
			if (match2.Success)
			{
				Stage(1, 1f, L("Pipelines: all ready", "Pipelines: todas prontas"), live: true);
			}
		});
		process2.WaitForExit();
		Stage(1, 1f, L("Pipelines: done", "Pipelines: feito"), live: false);
		Stage(0, 1f, L("Shaders: done", "Shaders: feito"), live: false);
		if (process2.ExitCode != 0)
		{
			Log(string.Format(L("Preparing pipelines stopped (code {0}); the shaders are ready, PLAY makes the pipelines.", "A preparacao das pipelines parou (codigo {0}); os shaders estao prontos, o JOGAR faz as pipelines."), process2.ExitCode));
			TellErrors(text, mark);
		}
		Log(L("Shaders ready. You can press PLAY.", "Shaders prontos. Podes carregar em JOGAR."));
		WriteSetting("shaders", "1");
		WriteSetting("shaders_driver", driverVersion);
		return true;
	}

	private bool RoomForCache()
	{
		try
		{
			Directory.CreateDirectory(ActiveCacheDir);
			DriveInfo driveInfo = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(ActiveCacheDir)));
			if (driveInfo.AvailableFreeSpace >= 12884901888L)
			{
				return true;
			}
			Log(string.Format(L("Only {0:0.0} GB free on {1}: the shaders need about 12 GB. Free some space and try again.", "So {0:0.0} GB livres em {1}: os shaders precisam de cerca de 12 GB. Liberta espaco e tenta de novo."), (double)driveInfo.AvailableFreeSpace / 1073741824.0, driveInfo.Name));
			return false;
		}
		catch (Exception)
		{
			return true;
		}
	}

	private void TellErrors(string log, long mark)
	{
		try
		{
			using FileStream fileStream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			fileStream.Seek(Math.Min(mark, fileStream.Length), SeekOrigin.Begin);
			List<string> list = (from s in new StreamReader(fileStream).ReadToEnd().Split('\n')
				select s.TrimEnd('\r') into s
				where Regex.IsMatch(s, "(?i)\\b(error|panicked|failed|denied|not found|no space)\\b")
				select s).Reverse().Take(8).Reverse()
				.ToList();
			foreach (string item in list)
			{
				Log("   " + ((item.Length > 220) ? (item.Substring(0, 220) + "...") : item));
			}
			if (list.Count == 0)
			{
				Log(L("   (no error lines; the whole log: ", "   (sem linhas de erro; o log todo: ") + log + ")");
			}
		}
		catch (Exception)
		{
		}
	}

	private bool ShadersReady()
	{
		return CountUpTo(Path.Combine(ActiveCacheDir, "shaders"), 1000) >= 1000;
	}

	private static int CountUpTo(string dir, int most)
	{
		int num = 0;
		Stack<string> stack = new Stack<string>();
		stack.Push(dir);
		while (stack.Count > 0 && num < most)
		{
			string path = stack.Pop();
			try
			{
				foreach (string item in Directory.EnumerateFiles(path))
				{
					_ = item;
					if (++num >= most)
					{
						break;
					}
				}
			}
			catch (Exception)
			{
			}
			try
			{
				foreach (string item2 in Directory.EnumerateDirectories(path))
				{
					stack.Push(item2);
				}
			}
			catch (Exception)
			{
			}
		}
		return num;
	}

	private bool FindGame()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		List<string> list = new List<string>();
		list.Add(Path.Combine(folderPath, "Downloads"));
		list.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
		list.Add(Environment.GetFolderPath(Environment.SpecialFolder.Personal));
		list.Add(Path.Combine(folderPath, "Games"));
		List<string> list2 = list;
		DriveInfo[] drives = DriveInfo.GetDrives();
		foreach (DriveInfo driveInfo in drives)
		{
			if (driveInfo.DriveType == DriveType.Fixed && driveInfo.IsReady)
			{
				list2.Add(driveInfo.RootDirectory.FullName);
				list2.Add(Path.Combine(driveInfo.RootDirectory.FullName, "Games"));
			}
		}
		Progress(L("Looking for game_scarlett*.exe...", "A procurar o game_scarlett*.exe..."), 0.3f);
		string loose = null;
		foreach (string item in list2.Distinct())
		{
			string text = Search(item, 4, ref loose);
			if (text != null)
			{
				return UseGameFolder(Path.GetDirectoryName(text));
			}
		}
		if (loose != null)
		{
			UseGameFolder(Path.GetDirectoryName(loose));
			Log(MissingDumpText(Path.GetDirectoryName(loose)));
			return false;
		}
		Log(L("The game was not found: choose its folder with Browse on the Play page.", "O jogo nao foi encontrado: escolhe a pasta com Procurar na pagina Jogar."));
		return false;
	}

	private bool UseGameFolder(string dir)
	{
		Invoke((Action)(() =>
		{
			gameBox.Text = dir;
			SaveSettings();
		}));
		Log(L("Game found: ", "Jogo encontrado: ") + dir);
		return true;
	}

	private static string[] MissingDumpFiles(string dir)
	{
		try
		{
			return DumpFiles.Where((string f) => !File.Exists(Path.Combine(dir, f))).ToArray();
		}
		catch (Exception)
		{
			return DumpFiles;
		}
	}

	private static string MissingDumpText(string dir)
	{
		return string.Format(L("The game's files are not beside the executable in {0} ({1} missing). Move the whole dump (game_scarlett_master.exe with its .rpf files and its data and pack folders) into a folder of its own, for example C:\\Games\\GTA6, and choose that folder on the Play page.", "Os ficheiros do jogo nao estao ao lado do executavel em {0} (falta {1}). Move o dump inteiro (o game_scarlett_master.exe com os ficheiros .rpf e as pastas data e pack) para uma pasta so dele, por exemplo C:\\Games\\GTA6, e escolhe essa pasta na pagina Jogar.", "Les fichiers du jeu ne sont pas à côté de l'exécutable dans {0} ({1} manquant). Déplace tout le dump (game_scarlett_master.exe avec ses fichiers .rpf et ses dossiers data et pack) dans un dossier à lui, par exemple C:\\Games\\GTA6, et choisis ce dossier dans la page Jouer."), dir, string.Join(", ", MissingDumpFiles(dir)));
	}

	private static string Search(string dir, int depth, ref string loose)
	{
		try
		{
			if (!Directory.Exists(dir))
			{
				return null;
			}
			string text = Path.Combine(dir, "game_scarlett_master.exe");
			string[] array = GameExes(dir);
			string text2;
			if (File.Exists(text))
			{
				text2 = text;
			}
			else
			{
				text2 = ((array.Length > 0) ? Path.Combine(dir, array[0]) : null);
			}
			if (text2 != null)
			{
				if (MissingDumpFiles(dir).Length == 0)
				{
					return text2;
				}
				if (loose == null)
				{
					loose = text2;
				}
			}
			if (depth == 0)
			{
				return null;
			}
			foreach (string item in Directory.EnumerateDirectories(dir))
			{
				string fileName = Path.GetFileName(item);
				if (!fileName.StartsWith("$") && !fileName.Equals("Windows", StringComparison.OrdinalIgnoreCase) && !fileName.StartsWith("Program", StringComparison.OrdinalIgnoreCase) && !fileName.Equals("AppData", StringComparison.OrdinalIgnoreCase))
				{
					string text3 = Search(item, depth - 1, ref loose);
					if (text3 != null)
					{
						return text3;
					}
				}
			}
		}
		catch (Exception)
		{
		}
		return null;
	}

	private static bool BestPower()
	{
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes");
			return ((registryKey == null) ? null : (registryKey.GetValue("ActiveOverlayAcPowerScheme") as string))?.Equals("ded574b5-45a0-4f42-8737-46345c09c238", StringComparison.OrdinalIgnoreCase) ?? false;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static bool PagefileOk()
	{
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management");
			string[] array = ((registryKey == null) ? null : (registryKey.GetValue("PagingFiles") as string[]));
			if (array == null)
			{
				return false;
			}
			long num = 0L;
			string[] array2 = array;
			foreach (string text in array2)
			{
				string[] array3 = text.Trim().Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (array3.Length != 0)
				{
					if (array3[0].StartsWith("?:", StringComparison.Ordinal))
					{
						return true;
					}
					if (array3.Length == 1 || (array3.Length >= 3 && array3[1] == "0" && array3[2] == "0"))
					{
						return true;
					}
					if (array3.Length >= 3 && long.TryParse(array3[2], out var result))
					{
						num += result;
					}
				}
			}
			return num >= 32768;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static bool SetManagedPagefile()
	{
		bool result = false;
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
			foreach (ManagementObject item in managementObjectSearcher.Get())
			{
				item["AutomaticManagedPagefile"] = true;
				item.Put();
				result = true;
			}
		}
		catch (Exception)
		{
		}
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management", writable: true);
			if (registryKey != null)
			{
				registryKey.SetValue("PagingFiles", new string[1] { "?:\\pagefile.sys" }, RegistryValueKind.MultiString);
				result = true;
			}
		}
		catch (Exception)
		{
		}
		return result;
	}

	private bool FixPagefile()
	{
		if (Elevated(Application.ExecutablePath, "--pagefile") && PagefileOk())
		{
			Log(L("Virtual memory is now managed by Windows. Restart Windows for it to count.", "A memoria virtual passou a ser gerida pelo Windows. Reinicia o Windows para contar."));
			return true;
		}
		Log(L("Virtual memory could not be set. By hand: Windows key > 'Advanced system settings' > Performance: Settings > Advanced > Virtual memory: Change > tick 'Automatically manage paging file size for all drives' > OK, then restart.", "A memoria virtual nao ficou definida. A mao: tecla Windows > 'Definicoes avancadas do sistema' > Desempenho: Definicoes > Avancadas > Memoria virtual: Alterar > marca 'Gerir automaticamente o tamanho do ficheiro de paginacao para todas as unidades' > OK e reinicia."));
		try
		{
			Process.Start("SystemPropertiesPerformance.exe");
		}
		catch (Exception)
		{
		}
		return false;
	}

	private bool Elevated(string file, string args)
	{
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo(file, args);
			processStartInfo.Verb = "runas";
			processStartInfo.UseShellExecute = true;
			processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
			Process process = Process.Start(processStartInfo);
			process.WaitForExit();
			return process.ExitCode == 0;
		}
		catch (Win32Exception)
		{
			Log(L("Administrator rights were not given.", "Nao foram dadas permissoes de administrador."));
			return false;
		}
	}

	private static int RunWait(string file, string args, bool window)
	{
		try
		{
			bool flag = file == "taskkill" || file == "powercfg";
			ProcessStartInfo processStartInfo = new ProcessStartInfo(file, args);
			processStartInfo.UseShellExecute = false;
			processStartInfo.CreateNoWindow = !window;
			using Process process = Process.Start(processStartInfo);
			if (!process.WaitForExit(flag ? 30000 : (-1)))
			{
				try
				{
					process.Kill();
				}
				catch (Exception)
				{
				}
				return -1;
			}
			return process.ExitCode;
		}
		catch (Exception)
		{
			return -1;
		}
	}

	private static void OpenUrl(string url)
	{
		try
		{
			Process.Start(url);
		}
		catch (Exception)
		{
		}
	}

	private void WriteSetting(string key, string value)
	{
		try
		{
			lock (settingsLock)
			{
				List<string> list = (File.Exists(settingsFile) ? (from l in File.ReadAllLines(settingsFile)
					where !l.StartsWith(key + "=")
					select l).ToList() : new List<string>());
				list.Add(key + "=" + value);
				Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));
				WriteLinesAtomic(settingsFile, list.ToArray());
			}
		}
		catch (Exception)
		{
		}
	}

	private int RebarCardSupport()
	{
		string input = ((probeAdapter.Length > 0) ? probeAdapter : gpuName);
		if (Regex.IsMatch(input, "\\bGTX\\b|RTX\\s*20[5-8]0|TITAN\\s*(X|V|RTX)|\\bMX\\s*\\d|Quadro\\s+[KMP]\\d|\\bRX\\s*[45][5-9]0\\b|\\bVega\\b|\\bR[79]\\s", RegexOptions.IgnoreCase))
		{
			return 0;
		}
		if (Regex.IsMatch(input, "RTX\\s*30\\d\\d", RegexOptions.IgnoreCase))
		{
			return 2;
		}
		return 1;
	}

	private bool RebarMissing()
	{
		if (rebar || rebarBios || ReadSetting("rebar_ack") == "1" || integratedOnly || RebarCardSupport() == 0)
		{
			return false;
		}
		if (hasNvidia && barRead)
		{
			return rebarHeap != 1;
		}
		if (rebarHeap == 0)
		{
			return probeOptions16;
		}
		return false;
	}

	private string[] RebarSteps(string maker)
	{
		string text = (maker ?? "").ToUpperInvariant();
		if (text.Contains("ASUS"))
		{
			return new string[2]
			{
				L("ASUS: Advanced > PCI Subsystem Settings > Above 4G Decoding: Enabled, then Re-Size BAR Support: Auto/Enabled.", "ASUS: Advanced > PCI Subsystem Settings > Above 4G Decoding: Enabled, depois Re-Size BAR Support: Auto/Enabled."),
				L("Boot > CSM (Compatibility Support Module) > Launch CSM: Disabled.", "Boot > CSM (Compatibility Support Module) > Launch CSM: Disabled.")
			};
		}
		if (text.Contains("MICRO-STAR") || text.Contains("MSI"))
		{
			return new string[2]
			{
				L("MSI: Settings > Advanced > PCI Subsystem Settings > Above 4G memory/Crypto Currency mining: Enabled, then Re-Size BAR Support: Enabled", "MSI: Settings > Advanced > PCI Subsystem Settings > Above 4G memory/Crypto Currency mining: Enabled, depois Re-Size BAR Support: Enabled"),
				L("(newer boards: the 'Smart Access Memory / Re-Size BAR' button on the EZ Mode page). Turn CSM off under Boot.", "(placas recentes: o botao 'Smart Access Memory / Re-Size BAR' na pagina EZ Mode). Desliga o CSM em Boot.")
			};
		}
		if (text.Contains("GIGABYTE"))
		{
			return new string[2]
			{
				L("Gigabyte/AORUS: Settings > IO Ports > Above 4G Decoding: Enabled, then Re-Size BAR Support: Auto.", "Gigabyte/AORUS: Settings > IO Ports > Above 4G Decoding: Enabled, depois Re-Size BAR Support: Auto."),
				L("Boot > CSM Support: Disabled.", "Boot > CSM Support: Disabled.")
			};
		}
		if (text.Contains("ASROCK"))
		{
			return new string[2]
			{
				L("ASRock: Advanced > PCI Configuration > Above 4G Decoding: Enabled, then Re-Size BAR Support: Enabled.", "ASRock: Advanced > PCI Configuration > Above 4G Decoding: Enabled, depois Re-Size BAR Support: Enabled."),
				L("Boot > CSM (Compatibility Support Module): Disabled.", "Boot > CSM (Compatibility Support Module): Disabled.")
			};
		}
		return new string[2]
		{
			L("Look for 'Above 4G Decoding' and 'Re-Size BAR' (or 'Resizable BAR', 'Smart Access Memory') under Advanced > PCI, and turn CSM off under Boot.", "Procura 'Above 4G Decoding' e 'Re-Size BAR' (ou 'Resizable BAR', 'Smart Access Memory') em Advanced > PCI, e desliga o CSM em Boot."),
			L("Laptops (Acer, Lenovo, HP, Dell...): usually no switch; it comes with the maker's BIOS update for your model.", "Portateis (Acer, Lenovo, HP, Dell...): normalmente nao ha opcao; vem com a atualizacao de BIOS do fabricante para o teu modelo.")
		};
	}

	private Form Dialog(string title, int width, int height)
	{
		Form f = new Form
		{
			Text = title,
			ClientSize = new Size(width, height),
			BackColor = Theme.Back,
			ForeColor = Theme.Text,
			Font = Theme.Ui(9.75f),
			StartPosition = FormStartPosition.CenterParent,
			FormBorderStyle = FormBorderStyle.FixedDialog,
			MaximizeBox = false,
			MinimizeBox = false,
			ShowInTaskbar = false,
			Icon = Icon
		};
		f.HandleCreated += (object s, EventArgs e) =>
		{
			int value = 1;
			try
			{
				if (DwmSetWindowAttribute(f.Handle, 20, ref value, 4) != 0)
				{
					DwmSetWindowAttribute(f.Handle, 19, ref value, 4);
				}
			}
			catch (Exception)
			{
			}
			int value2 = Theme.Side.R | (Theme.Side.G << 8) | (Theme.Side.B << 16);
			try
			{
				DwmSetWindowAttribute(f.Handle, 35, ref value2, 4);
			}
			catch (Exception)
			{
			}
		};
		return f;
	}

	private void ShowRebarGuide()
	{
		bool flag = Uefi();
		string text = ((hw != null) ? hw.BoardMaker : "");
		string text2 = ((hw != null) ? hw.Board : "");
		if (hw == null)
		{
			try
			{
				text = Wmi("SELECT Manufacturer FROM Win32_BaseBoard", "Manufacturer");
				text2 = Wmi("SELECT Product FROM Win32_BaseBoard", "Product");
			}
			catch (Exception)
			{
			}
		}
		int num = RebarCardSupport();
		Form f = Dialog("Resizable BAR", 860, 720);
		f.Controls.Add(new Heading(L("Resizable BAR", "Resizable BAR", "Resizable BAR"))
		{
			Location = new Point(24, 18),
			Width = 810
		});
		Func<bool, string, string> func = (bool ok, string what) => (ok ? "✓  " : "✗  ") + what;
		List<string> list = new List<string>();
		List<string> list2 = list;
		Func<bool, string, string> func2 = func;
		bool arg = rebar;
		string arg2;
		if (rebarHeap == 1)
		{
			arg2 = L("The game gets it: D3D12 offers mapped video memory.", "O jogo tem-no: o D3D12 oferece memoria de video mapeada.");
		}
		else
		{
			arg2 = ((rebarHeap == 0 && rebarBios) ? L("On in the BIOS and on the card, but Windows does not offer it to games (see 3).", "Ligado na BIOS e na placa, mas o Windows nao o oferece aos jogos (ve o 3).") : L("The game does not get it.", "O jogo nao o tem."));
		}
		list2.Add(func2(arg, arg2));
		List<string> list3 = list;
		Func<bool, string, string> func3 = func;
		bool arg3 = rebarBios || !hasNvidia;
		string arg4;
		if (hasNvidia)
		{
			arg4 = (rebarBios ? L("The card reports a large BAR (nvidia-smi: BAR1 of 1 GB or more).", "A placa indica um BAR grande (nvidia-smi: BAR1 de 1 GB ou mais).") : L("The card reports a 256 MB BAR: off in the BIOS, or the card's firmware lacks it.", "A placa indica um BAR de 256 MB: desligado na BIOS, ou o firmware da placa nao o tem."));
		}
		else
		{
			arg4 = L("Not an NVIDIA card: only the game's side is checked.", "Nao e uma placa NVIDIA: so o lado do jogo e verificado.");
		}
		list3.Add(func3(arg3, arg4));
		list.Add(func(flag, flag ? L("The PC starts in UEFI mode (needed).", "O PC arranca em modo UEFI (necessario).") : L("The PC starts in Legacy/CSM mode: Resizable BAR needs UEFI (convert the disk with mbr2gpt first).", "O PC arranca em Legacy/CSM: o Resizable BAR precisa de UEFI (converte o disco com o mbr2gpt primeiro).")));
		list.Add(func(num != 0, num switch
		{
			2 => string.Format(L("{0}: early RTX 30 cards need NVIDIA's Resizable BAR firmware update (see 2).", "{0}: as primeiras RTX 30 precisam da atualizacao de firmware Resizable BAR da NVIDIA (ve o 2)."), gpuName), 
			0 => string.Format(L("{0} cannot use Resizable BAR (only RTX 30 and newer, RX 5000 and newer).", "A {0} nao suporta Resizable BAR (so RTX 30 ou mais recente, RX 5000 ou mais recente)."), gpuName), 
			_ => string.Format(L("{0} supports it.", "A {0} suporta-o."), gpuName), 
		}));
		List<string> list4 = list;
		List<string> list5 = new List<string>();
		list5.Add(L("1.  Motherboard: ", "1.  Placa-mae: ") + (text + " " + text2).Trim() + L(". Update its BIOS first, then in the BIOS:", ". Atualiza a BIOS primeiro, depois na BIOS:"));
		string[] array = RebarSteps(text);
		foreach (string text3 in array)
		{
			list5.Add("       " + text3);
		}
		list5.Add(L("       Save and exit (usually F10). Back in Windows, press 'Check again'.", "       Guarda e sai (normalmente F10). De volta ao Windows, carrega em 'Verificar de novo'."));
		list5.Add(L("2.  Graphics card: update the driver. RTX 3060/3060 Ti/3070/3080/3090 bought before April 2021 may need NVIDIA's", "2.  Placa grafica: atualiza o driver. RTX 3060/3060 Ti/3070/3080/3090 compradas antes de abril de 2021 podem precisar da"));
		list5.Add(L("       Resizable BAR firmware update (button below; from your card maker's site for some models).", "       atualizacao de firmware Resizable BAR da NVIDIA (botao em baixo; no site do fabricante da placa para alguns modelos)."));
		list5.Add(L("3.  Windows: games get it through D3D12's GPU upload heaps: Windows 11 24H2 or newer and a recent driver.", "3.  Windows: os jogos tem-no pelos GPU upload heaps do D3D12: Windows 11 24H2 ou mais recente e um driver recente."));
		list5.Add(L("       On but not offered: Windows Update (to 24H2+) and the newest NVIDIA/AMD driver, then restart.", "       Ligado mas nao oferecido: Windows Update (para 24H2+) e o driver NVIDIA/AMD mais recente, depois reinicia."));
		list5.Add("");
		list5.Add(L("If you cannot turn it on (older card, laptop without the option, Windows 10):", "Se nao o conseguires ligar (placa antiga, portatil sem a opcao, Windows 10):"));
		list5.Add(L("   •  The game still works: the launcher leaves the direct video-memory writes off on its own.", "   •  O jogo funciona na mesma: o launcher deixa a escrita direta na VRAM desligada sozinho."));
		list5.Add(L("   •  Pick Modest PC or Ultra Low, keep 'Keep the most video memory' on, and close apps on the Performance page:", "   •  Escolhe PC modesto ou Ultra baixo, deixa 'Guardar o maximo de VRAM' ligado e fecha apps na pagina Desempenho:"));
		list5.Add(L("      without Resizable BAR the copies to the card are slower, so less to copy helps most.", "      sem Resizable BAR as copias para a placa sao mais lentas, por isso copiar menos e o que mais ajuda."));
		list5.Add(L("   •  'I can't turn it on' stops the launcher warning you about it.", "   •  'Nao o consigo ligar' faz o launcher deixar de te avisar."));
		int num3 = f.ClientSize.Width - 48;
		Panel panel = new Panel();
		panel.Location = new Point(24, 58);
		panel.Size = new Size(num3, f.ClientSize.Height - 58 - 76);
		panel.AutoScroll = true;
		panel.BackColor = Theme.Back;
		Panel panel2 = panel;
		Kit.Dark(panel2, "DarkMode_Explorer");
		f.Controls.Add(panel2);
		int num4 = num3 - SystemInformation.VerticalScrollBarWidth - 6;
		int num5 = num4 - 40;
		RoundCard roundCard = new RoundCard();
		roundCard.Location = new Point(0, 0);
		roundCard.Width = num4;
		RoundCard roundCard2 = roundCard;
		int num6 = 16;
		Font f2 = Theme.Ui(9.5f);
		foreach (string item in list4)
		{
			bool flag2 = item.StartsWith("✓");
			string text4 = item.Substring(Math.Min(3, item.Length));
			StatusMark statusMark = new StatusMark();
			statusMark.Location = new Point(16, num6);
			statusMark.Size = new Size(22, 22);
			statusMark.Kind = (flag2 ? 1 : 0);
			StatusMark value = statusMark;
			Label label = Kit.Text(text4, f2, flag2 ? Theme.Soft : Theme.Warn, Theme.Card, 48, num6 + 1, num4 - 48 - 16);
			roundCard2.Controls.Add(value);
			roundCard2.Controls.Add(label);
			num6 += Math.Max(26, label.Height + 2) + 6;
		}
		roundCard2.Height = num6 + 10;
		panel2.Controls.Add(roundCard2);
		RoundCard roundCard3 = new RoundCard();
		roundCard3.Location = new Point(0, roundCard2.Height + 12);
		roundCard3.Width = num4;
		RoundCard roundCard4 = roundCard3;
		int num7 = 16;
		foreach (string item2 in list5)
		{
			if (item2.Trim().Length == 0)
			{
				num7 += 10;
				continue;
			}
			int num8 = item2.Length - item2.TrimStart().Length;
			string text5 = item2.TrimStart();
			bool flag3 = num8 == 0;
			int num9 = ((!flag3) ? ((num8 >= 6) ? 26 : 12) : 0);
			Label label2 = Kit.Text(text5, flag3 ? Theme.UiBold(9.75f) : Theme.Ui(9.5f), flag3 ? Theme.Text : Theme.Soft, Theme.Card, 20 + num9, num7, num5 - num9);
			roundCard4.Controls.Add(label2);
			num7 += label2.Height + (flag3 ? 2 : 0);
		}
		roundCard4.Height = num7 + 14;
		panel2.Controls.Add(roundCard4);
		panel2.Controls.Add(new Label
		{
			Location = new Point(0, roundCard4.Bottom),
			Size = new Size(10, 8),
			BackColor = Theme.Back
		});
		Pill pill = new Pill();
		pill.Main = true;
		pill.Text = L("Restart into the BIOS", "Reiniciar para a BIOS", "Redémarrer dans le BIOS");
		pill.Size = new Size(200, 40);
		pill.Enabled = flag && !rebar;
		pill.Font = Theme.Ui(10f);
		Pill pill2 = pill;
		pill2.Click += (object s, EventArgs e) =>
		{
			string text6 = L("Save your work first. The PC restarts in 5 seconds and opens its BIOS/UEFI settings. Restart now?", "Guarda o teu trabalho primeiro. O PC reinicia em 5 segundos e abre as definicoes da BIOS/UEFI. Reiniciar agora?", "Enregistre ton travail d'abord. Le PC redémarre dans 5 secondes et ouvre les réglages du BIOS/UEFI. Redémarrer maintenant ?");
			if (MessageBox.Show(f, text6, "Resizable BAR", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
			{
				Thread thread = new Thread(() =>
				{
					Elevated("shutdown", "/r /fw /t 5");
				});
				thread.IsBackground = true;
				thread.Start();
			}
		};
		List<Pill> list6 = new List<Pill>();
		list6.Add(pill2);
		List<Pill> list7 = list6;
		if (num == 2)
		{
			Pill pill3 = new Pill();
			pill3.Text = L("NVIDIA firmware update", "Firmware da NVIDIA", "Firmware NVIDIA");
			pill3.Size = new Size(180, 40);
			Pill pill4 = pill3;
			pill4.Click += (object s, EventArgs e) =>
			{
				OpenUrl("https://www.nvidia.com/en-us/geforce/news/geforce-rtx-30-series-resizable-bar-support/");
			};
			list7.Add(pill4);
		}
		Pill pill5 = new Pill();
		pill5.Text = L("Check again", "Verificar de novo", "Vérifier à nouveau");
		pill5.Size = new Size(150, 40);
		Pill pill6 = pill5;
		pill6.Click += (object s, EventArgs e) =>
		{
			f.Close();
			DetectSystem();
			ProbeGpu();
			CheckAll();
		};
		list7.Add(pill6);
		if (!rebar)
		{
			Pill pill7 = new Pill();
			pill7.Text = L("I can't turn it on", "Nao o consigo ligar", "Je ne peux pas l'activer");
			pill7.Size = new Size(170, 40);
			Pill pill8 = pill7;
			pill8.Click += (object s, EventArgs e) =>
			{
				WriteSetting("rebar_ack", "1");
				Log(L("Resizable BAR: no more warnings. The game runs without it.", "Resizable BAR: sem mais avisos. O jogo corre sem ele."));
				f.Close();
				CheckAll();
				Refresh2();
			};
			list7.Add(pill8);
		}
		Panel bar = new Panel
		{
			Dock = DockStyle.Bottom,
			Height = 76,
			BackColor = Theme.Back
		};
		bar.Paint += (object s, PaintEventArgs e) =>
		{
			using Pen pen = new Pen(Theme.Line);
			e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0);
		};
		f.Controls.Add(bar);
		Kit.Row(bar, 24, 18, f.ClientSize.Width - 48, list7.ToArray());
		f.ShowDialog(this);
	}

	[DllImport("user32.dll")]
	private static extern bool IsHungAppWindow(IntPtr hwnd);

	private GameState State()
	{
		GameState result = GameState.None;
		Process process = gameProc;
		Process[] processesByName = Process.GetProcessesByName("victory");
		foreach (Process process2 in processesByName)
		{
			try
			{
				bool flag = process != null && !process.HasExited && process2.Id == process.Id;
				IntPtr mainWindowHandle = process2.MainWindowHandle;
				if (flag)
				{
					if (mainWindowHandle != IntPtr.Zero)
					{
						gameHadWindow = true;
						windowGone = null;
					}
					else if (gameHadWindow && !windowGone.HasValue)
					{
						windowGone = DateTime.Now;
					}
				}
				if (flag || !(mainWindowHandle == IntPtr.Zero))
				{
					if ((mainWindowHandle != IntPtr.Zero && IsHungAppWindow(mainWindowHandle)) || (flag && windowGone.HasValue && (DateTime.Now - windowGone.Value).TotalSeconds > 15.0))
					{
						return GameState.Stuck;
					}
					result = GameState.Running;
				}
			}
			catch (Exception)
			{
			}
			finally
			{
				process2.Dispose();
			}
		}
		return result;
	}

	private bool Ours(int id)
	{
		Func<Process, bool> func = (Process p) =>
		{
			try
			{
				return p != null && !p.HasExited && p.Id == id;
			}
			catch (Exception)
			{
				return false;
			}
		};
		if (func(gameProc) || func(afterWarm))
		{
			return true;
		}
		lock (tools)
		{
			return tools.Any(func);
		}
	}

	private void SafeLog(string line)
	{
		try
		{
			Log(line);
		}
		catch (Exception)
		{
		}
	}

	private List<int> OtherPreparations()
	{
		List<int> list = new List<int>();
		try
		{
			List<Tuple<int, int, string>> rows = new List<Tuple<int, int, string>>();
			using (ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process WHERE Name = 'victory.exe'"))
			{
				using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
				foreach (ManagementObject item in managementObjectCollection)
				{
					using (item)
					{
						rows.Add(Tuple.Create(Convert.ToInt32(item["ProcessId"]), Convert.ToInt32(item["ParentProcessId"]), (item["CommandLine"] as string) ?? ""));
					}
				}
			}
			Func<int, bool> func = (int id) =>
			{
				for (int i = 0; i < 16; i++)
				{
					if (Ours(id))
					{
						return true;
					}
					Tuple<int, int, string> tuple = rows.FirstOrDefault((Tuple<int, int, string> r) => r.Item1 == id);
					if (tuple == null)
					{
						return false;
					}
					id = tuple.Item2;
				}
				return false;
			};
			foreach (Tuple<int, int, string> item2 in rows)
			{
				if (Regex.IsMatch(item2.Item3, "\\s(shaders|warm|precompile)(\\s|$)") && !func(item2.Item1))
				{
					list.Add(item2.Item1);
				}
			}
		}
		catch (Exception)
		{
		}
		return list;
	}

	private bool ClearOtherPreparations()
	{
		List<int> list = OtherPreparations();
		if (list.Count == 0)
		{
			return true;
		}
		string text = L("Shaders are still being prepared by another launcher window (or one that was closed). Two at once run slowly and can damage the cache.\r\n\r\nStop it and go on? Nothing is lost: what is already translated stays in the cache.", "Os shaders ainda estao a ser preparados por outra janela do launcher (ou uma que foi fechada). Dois ao mesmo tempo correm devagar e podem estragar a cache.\r\n\r\nParar e continuar? Nada se perde: o que ja foi traduzido fica na cache.", "Les shaders sont encore en préparation par une autre fenêtre du launcher (ou une fenêtre fermée). Deux à la fois sont lents et peuvent abîmer le cache.\r\n\r\nL'arrêter et continuer ? Rien n'est perdu : ce qui est déjà traduit reste dans le cache.");
		if (MessageBox.Show(this, text, "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			return false;
		}
		List<int> list2 = OtherPreparations();
		foreach (int item in list)
		{
			if (list2.Contains(item))
			{
				try
				{
					RunWait("taskkill", "/PID " + item + " /T /F", window: false);
				}
				catch (Exception)
				{
				}
			}
		}
		Log(L("Stopped a preparation left running by another launcher window.", "Parada uma preparacao deixada a correr por outra janela do launcher."));
		return true;
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		base.OnFormClosing(e);
		if (e.Cancel)
		{
			return;
		}
		List<Process> list;
		lock (tools)
		{
			list = tools.Where((Process t) =>
			{
				try
				{
					return !t.HasExited;
				}
				catch (Exception)
				{
					return false;
				}
			}).ToList();
		}
		if (list.Count == 0)
		{
			return;
		}
		if (e.CloseReason == CloseReason.UserClosing && MessageBox.Show(this, L("Shaders or pipelines are still being prepared. Stop and close? It carries on where it stopped next time.", "Os shaders ou as pipelines ainda estao a ser preparados. Parar e fechar? Continua onde parou da proxima vez.", "Les shaders ou les pipelines sont encore en préparation. Arrêter et fermer ? Ça reprend là où ça s'est arrêté la prochaine fois."), "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			e.Cancel = true;
			return;
		}
		foreach (Process item in list)
		{
			try
			{
				if (!item.HasExited)
				{
					RunWait("taskkill", "/PID " + item.Id + " /T /F", window: false);
				}
			}
			catch (Exception)
			{
			}
		}
	}

	private void PipelineHint()
	{
		if (!pipelineHintShown && warmBox.Checked)
		{
			string path = Path.Combine(ActiveCacheDir, "pipelines");
			if (Lines(Path.Combine(path, "warm-done.txt")) <= 0)
			{
				pipelineHintShown = true;
				MessageBox.Show(this, L("First game on this engine: its pipelines are not built yet, so the first minutes stutter and some objects are missing while your graphics driver compiles them.\r\n\r\nThis game writes them down. To have them built before the game opens:\r\n  1.  Play 5-10 minutes in the world, then close the game.\r\n  2.  Wait until Activity says \"Done\" (the launcher builds them).\r\n  3.  Start the game again.", "Primeiro jogo neste motor: as pipelines ainda nao foram feitas, por isso os primeiros minutos engasgam e faltam objetos enquanto o driver as compila.\r\n\r\nEste jogo regista-as. Para serem feitas antes do jogo abrir:\r\n  1.  Joga 5-10 minutos no mundo e fecha o jogo.\r\n  2.  Espera que a Atividade diga \"Feito\" (o launcher fa-las).\r\n  3.  Abre o jogo outra vez.", "Premier jeu sur ce moteur : ses pipelines ne sont pas encore faites, donc les premières minutes saccadent et des objets manquent pendant que ton pilote les compile.\r\n\r\nCe jeu les note. Pour qu'elles soient faites avant l'ouverture du jeu :\r\n  1.  Joue 5-10 minutes dans le monde, puis ferme le jeu.\r\n  2.  Attends que l'Activité dise « Terminé » (le launcher les fait).\r\n  3.  Relance le jeu."), "Julia's Launcher", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			}
		}
	}

	private bool HeavyAppsCheck()
	{
		if (heavyAppsAsked)
		{
			return true;
		}
		double num;
		try
		{
			num = (double)new ComputerInfo().AvailablePhysicalMemory / 1073741824.0;
		}
		catch (Exception)
		{
			return true;
		}
		if (num >= 10.0)
		{
			return true;
		}
		List<Tuple<string, double>> list = new List<Tuple<string, double>>();
		string[] heavyApps = HeavyApps;
		foreach (string text in heavyApps)
		{
			long num2 = 0L;
			string text2 = null;
			Process[] processesByName = Process.GetProcessesByName(text);
			foreach (Process process in processesByName)
			{
				try
				{
					num2 += process.PrivateMemorySize64;
					if (text2 == null)
					{
						text2 = process.ProcessName;
					}
				}
				catch (Exception)
				{
				}
				finally
				{
					process.Dispose();
				}
			}
			if (num2 > 536870912)
			{
				list.Add(Tuple.Create(text2 ?? text, (double)num2 / 1073741824.0));
			}
		}
		if (list.Count == 0)
		{
			return true;
		}
		heavyAppsAsked = true;
		string arg = string.Join("\r\n", (from h in list
			orderby h.Item2 descending
			select $"  •  {h.Item1}: {h.Item2:0.0} GB").ToArray());
		string text3 = string.Format(L("Only {0:0.0} GB of RAM is free, and the game uses about 20 GB in the world. Short of RAM, Windows swaps to disk and textures load slowly.\r\n\r\nThese apps hold memory:\r\n{1}\r\n\r\nClose them first for a smoother game. Play anyway?", "So ha {0:0.0} GB de RAM livre e o jogo usa cerca de 20 GB no mundo. Sem RAM, o Windows usa o disco e as texturas carregam devagar.\r\n\r\nEstas apps ocupam memoria:\r\n{1}\r\n\r\nFecha-as primeiro para um jogo mais fluido. Jogar na mesma?", "Seulement {0:0.0} Go de RAM libre, et le jeu en utilise environ 20 Go dans le monde. À court de RAM, Windows passe par le disque et les textures arrivent lentement.\r\n\r\nCes applis occupent de la mémoire :\r\n{1}\r\n\r\nFerme-les d'abord pour un jeu plus fluide. Jouer quand même ?"), num, arg);
		return MessageBox.Show(this, text3, "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes;
	}

	private void CloseGames()
	{
		List<int> list = new List<int>();
		Process process = gameProc;
		Process process2 = afterWarm;
		Process[] processesByName = Process.GetProcessesByName("victory");
		foreach (Process process3 in processesByName)
		{
			try
			{
				bool flag = process != null && !process.HasExited && process3.Id == process.Id;
				bool flag2 = process2 != null && !process2.HasExited && process3.Id == process2.Id;
				if (flag || flag2 || process3.MainWindowHandle != IntPtr.Zero)
				{
					list.Add(process3.Id);
				}
			}
			catch (Exception)
			{
			}
			finally
			{
				process3.Dispose();
			}
		}
		gameProc = null;
		afterWarm = null;
		windowGone = null;
		gameHadWindow = false;
		foreach (int item in list)
		{
			RunWait("taskkill", "/PID " + item + " /T /F", window: false);
			Log(string.Format(L("Closed VIctory (process {0}).", "VIctory fechado (processo {0})."), item));
		}
	}

	private void StartChecks()
	{
		Thread thread = new Thread(() =>
		{
			while (!IsDisposed)
			{
				try
				{
					RunChecks();
				}
				catch (Exception)
				{
				}
				try
				{
					BeginInvoke(new Action(Refresh2));
				}
				catch (Exception)
				{
					break;
				}
				Thread.Sleep(8000);
			}
		});
		thread.IsBackground = true;
		thread.Priority = ThreadPriority.BelowNormal;
		thread.Start();
	}

	private void RunChecks()
	{
		string game = null;
		Invoke((Action)(() =>
		{
			game = GameExe;
		}));
		exeProblem = ExeProblem(game);
		shaderFiles = CountUpTo(Path.Combine(ActiveCacheDir, "shaders"), int.MaxValue);
		shadersOk = ShadersReady();
		pagefileOk = PagefileOk();
		powerOk = BestPower() && SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Offline;
		gpuPrefOk = GpuPreferenceOk();
	}

	private string ExeProblem(string path)
	{
		try
		{
			if (path == null || !File.Exists(path))
			{
				return null;
			}
			FileInfo fileInfo = new FileInfo(path);
			byte[] array = new byte[2];
			int num;
			using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				num = fileStream.Read(array, 0, 2);
			}
			if (fileInfo.Length < 1048576 || num < 2 || array[0] != 77 || array[1] != 90)
			{
				return string.Format(L("{0} is not a working program ({1:N0} bytes): the dump is incomplete or still encrypted. Download or extract it again.", "{0} nao e um programa que funcione ({1:N0} bytes): o dump esta incompleto ou ainda encriptado. Volta a descarregar ou extrair."), Path.GetFileName(path), fileInfo.Length);
			}
			long num2 = 0L;
			foreach (string item in Directory.EnumerateFiles(Path.GetDirectoryName(path), "*", System.IO.SearchOption.AllDirectories))
			{
				try
				{
					num2 += new FileInfo(item).Length;
				}
				catch (Exception)
				{
				}
				if (num2 >= 21474836480L)
				{
					break;
				}
			}
			if (num2 < 21474836480L)
			{
				return string.Format(L("This game folder holds only {0:0.0} GB: the game's data is not in it (the whole dump is about 100 GB). Choose the folder of the complete dump, with all its files beside game_scarlett_master.exe.", "Esta pasta do jogo so tem {0:0.0} GB: os dados do jogo nao estao nela (o dump completo tem cerca de 100 GB). Escolhe a pasta do dump completo, com todos os ficheiros ao lado do game_scarlett_master.exe."), (double)num2 / 1073741824.0);
			}
			if (MissingDumpFiles(Path.GetDirectoryName(path)).Length > 0)
			{
				return MissingDumpText(Path.GetDirectoryName(path));
			}
			string[] array2 = (from d in Directory.GetFiles(Path.GetDirectoryName(path), "*.dll")
				where new FileInfo(d).Length < 64
				select d).Select(Path.GetFileName).Take(3).ToArray();
			if (array2.Length > 0)
			{
				return string.Format(L("The dump has empty files ({0}): download or extract it again.", "O dump tem ficheiros vazios ({0}): volta a descarregar ou extrair."), string.Join(", ", array2));
			}
		}
		catch (Exception)
		{
		}
		return null;
	}

	private List<string> Missing()
	{
		List<string> list = new List<string>();
		if (integratedOnly)
		{
			list.Add(L("a graphics card with 4 GB or more (only integrated graphics found)", "uma placa grafica com 4 GB ou mais (so ha grafica integrada)", "une carte graphique de 4 Go ou plus (seulement un graphique intégré)"));
		}
		if (!pagefileOk)
		{
			list.Add(L("virtual memory managed by Windows", "memoria virtual gerida pelo Windows"));
		}
		if (!powerOk)
		{
			list.Add(L("best performance power (plugged in)", "energia em melhor desempenho (ligado a corrente)"));
		}
		if (RebarMissing())
		{
			list.Add("Resizable BAR");
		}
		if (!gpuPrefOk)
		{
			list.Add(L("the dedicated graphics card for VIctory", "a placa grafica dedicada para o VIctory"));
		}
		if (!shadersOk)
		{
			list.Add(L("prepared shaders", "shaders preparados"));
		}
		return list;
	}

	private static double CommitFreeGb()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT FreeVirtualMemory FROM Win32_OperatingSystem");
			using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectSearcher.Get().GetEnumerator();
			if (managementObjectEnumerator.MoveNext())
			{
				ManagementObject managementObject = (ManagementObject)managementObjectEnumerator.Current;
				return Convert.ToDouble(managementObject["FreeVirtualMemory"]) / 1048576.0;
			}
		}
		catch (Exception)
		{
		}
		return -1.0;
	}

	private double PagefileGrowthGb()
	{
		try
		{
			if (!PagefileOk())
			{
				return 0.0;
			}
			double num = 0.0;
			string driveName = "C:\\";
			using (ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT Name, AllocatedBaseSize FROM Win32_PageFileUsage"))
			{
				foreach (ManagementObject item in managementObjectSearcher.Get())
				{
					num += Convert.ToDouble(item["AllocatedBaseSize"]);
					string text = Convert.ToString(item["Name"]);
					if (!string.IsNullOrEmpty(text) && text.Length >= 3)
					{
						driveName = text.Substring(0, 3);
					}
				}
			}
			DriveInfo driveInfo = new DriveInfo(driveName);
			double num2 = 1073741824.0;
			double val = Math.Min(Math.Max(3.0 * ramGb, 4.0), (double)driveInfo.TotalSize / 8.0 / num2);
			double val2 = num / 1024.0 + Math.Max(0.0, (double)driveInfo.AvailableFreeSpace / num2 - 2.0);
			return Math.Max(0.0, Math.Min(val, val2) - num / 1024.0);
		}
		catch (Exception)
		{
			return 0.0;
		}
	}

	private bool EnoughMemory()
	{
		double num = CommitFreeGb();
		if (num < 0.0 || num >= 24.0)
		{
			return true;
		}
		double num2 = num + PagefileGrowthGb();
		if (num2 >= 24.0)
		{
			return true;
		}
		string text = string.Format(L("Windows can only promise {0:0.0} GB more memory (RAM plus virtual memory); the game needs about {1:0} GB as it starts. With less, its window stays white.\r\n\r\n•  Close apps (Performance page: what holds memory).\r\n•  Let Windows manage virtual memory (Setup), then restart the PC.\r\n\r\nPlay anyway?", "O Windows so pode prometer mais {0:0.0} GB de memoria (RAM mais memoria virtual); o jogo precisa de cerca de {1:0} GB ao arrancar. Com menos, a janela fica branca.\r\n\r\n•  Fecha apps (pagina Desempenho: o que ocupa memoria).\r\n•  Deixa o Windows gerir a memoria virtual (Preparar) e reinicia o PC.\r\n\r\nJogar na mesma?", "Windows ne peut garantir que {0:0.0} Go de mémoire en plus (RAM et mémoire virtuelle) ; le jeu a besoin d'environ {1:0} Go au démarrage. Avec moins, sa fenêtre reste blanche.\r\n\r\n•  Ferme des applis (page Performances : ce qui occupe la mémoire).\r\n•  Laisse Windows gérer la mémoire virtuelle (Installation), puis redémarre le PC.\r\n\r\nJouer quand même ?"), num2, 24.0);
		return MessageBox.Show(this, text, "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes;
	}

	private void WatchStart(string log, Process game)
	{
		Thread thread = new Thread(() =>
		{
			try
			{
				DateTime dateTime = DateTime.Now.AddSeconds(90.0);
				while (DateTime.Now < dateTime && !game.HasExited)
				{
					Thread.Sleep(2000);
					string text;
					try
					{
						using FileStream fileStream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
						if (fileStream.Length > 8388608)
						{
							fileStream.Seek(-8388608L, SeekOrigin.End);
						}
						text = new StreamReader(fileStream).ReadToEnd();
					}
					catch (Exception)
					{
						continue;
					}
					if (text.Contains("presenting to a window"))
					{
						break;
					}
					string why = null;
					if (text.Contains("no host D3D12 device") && text.Contains("8007000E"))
					{
						why = L("Windows ran out of memory to start the graphics (0x8007000E), so the window stays white.\r\n\r\nClose other apps, let Windows manage virtual memory (Setup) and restart the PC, then play again.", "O Windows ficou sem memoria para iniciar os graficos (0x8007000E), por isso a janela fica branca.\r\n\r\nFecha outras apps, deixa o Windows gerir a memoria virtual (Preparar) e reinicia o PC, depois joga de novo.");
					}
					else if (text.Contains("no host D3D12 device"))
					{
						why = L("The graphics card could not start D3D12, so the window stays white. Update the graphics driver and restart the PC.", "A placa grafica nao conseguiu iniciar o D3D12, por isso a janela fica branca. Atualiza o driver grafico e reinicia o PC.");
					}
					else if (text.Contains("malformed image"))
					{
						why = L("The game's files are damaged or incomplete (malformed image). Download or extract the dump again.", "Os ficheiros do jogo estao estragados ou incompletos (malformed image). Volta a descarregar ou extrair o dump.");
					}
					if (why != null)
					{
						try
						{
							BeginInvoke((Action)(() =>
							{
								Log(why.Replace("\r\n", " "));
								bool flag;
								try
								{
									flag = game.HasExited;
								}
								catch (Exception)
								{
									flag = true;
								}
								if (!flag && MessageBox.Show(this, why + L("\r\n\r\nClose the game now?", "\r\n\r\nFechar o jogo agora?"), "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Hand) == DialogResult.Yes)
								{
									CloseGames();
									Refresh2();
								}
							}));
							break;
						}
						catch (Exception)
						{
							break;
						}
					}
				}
			}
			catch (Exception)
			{
			}
		});
		thread.IsBackground = true;
		thread.Start();
	}

	private static string[] GameExes(string dir)
	{
		try
		{
			if (dir.Length == 0 || !Directory.Exists(dir))
			{
				return new string[0];
			}
			return (from n in (from f in Directory.GetFiles(dir, "game_scarlett*.exe")
					where f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
					select f).Select(Path.GetFileName)
				orderby (!n.Equals("game_scarlett_master.exe", StringComparison.OrdinalIgnoreCase)) ? 1 : 0
				select n).ThenBy((string n) => n, StringComparer.OrdinalIgnoreCase).ToArray();
		}
		catch (Exception)
		{
			return new string[0];
		}
	}

	private ComboBox ExeBox(int x, int y, int width)
	{
		exeBox = new Dropdown
		{
			Location = new Point(x, y),
			Width = width,
			Visible = false
		};
		exeBox.SelectedIndexChanged += (object s, EventArgs e) =>
		{
			if (!fillingExes && exeBox.SelectedItem != null)
			{
				string text = (string)exeBox.SelectedItem;
				if (!string.Equals(text, exeChoice, StringComparison.OrdinalIgnoreCase))
				{
					exeChoice = text;
					SaveSettings();
					Log(L("Game executable: ", "Executavel do jogo: ") + text);
					Refresh2();
				}
			}
		};
		return exeBox;
	}

	private void RefreshExes()
	{
		if (exeBox == null)
		{
			return;
		}
		string text = gameBox.Text.Trim();
		string[] array = GameExes(text);
		string text2 = text + "|" + string.Join("|", array) + "|" + exeChoice;
		if (!(text2 == exesOf))
		{
			bool flag = exesOf == null || !exesOf.StartsWith(text + "|" + string.Join("|", array) + "|");
			exesOf = text2;
			fillingExes = true;
			exeBox.Items.Clear();
			string[] array2 = array;
			foreach (string item in array2)
			{
				exeBox.Items.Add(item);
			}
			exeBox.Visible = array.Length > 1;
			if (array.Length > 0)
			{
				exeBox.SelectedItem = Path.GetFileName(GameExe);
			}
			fillingExes = false;
			if (array.Length > 1 && flag)
			{
				Log(string.Format(L("The game folder has {0} executables; using {1} (change it beside the folder).", "A pasta do jogo tem {0} executaveis; a usar o {1} (muda-o ao lado da pasta)."), array.Length, Path.GetFileName(GameExe)));
			}
		}
	}

	private void DetectCards()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility FROM Win32_VideoController");
			foreach (ManagementObject item in managementObjectSearcher.Get())
			{
				string text = ((item["Name"] as string) ?? "").Trim();
				if (text.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					hasNvidia = true;
				}
				if (Regex.IsMatch(text, "Radeon\\s*(RX|Pro|VII)", RegexOptions.IgnoreCase))
				{
					hasRadeon = true;
					radeonName = text;
				}
			}
		}
		catch (Exception)
		{
		}
		adapters = Gpus.List();
		integratedOnly = adapters.Count > 0 && adapters.TrueForAll((GpuAdapter a) => a.Integrated);
		GpuAdapter gpuAdapter = null;
		foreach (GpuAdapter adapter in adapters)
		{
			if (gpuAdapter == null || (gpuAdapter.Integrated && !adapter.Integrated) || (gpuAdapter.Integrated == adapter.Integrated && adapter.DedicatedMb > gpuAdapter.DedicatedMb))
			{
				gpuAdapter = adapter;
			}
		}
		if (gpuAdapter != null && vramGb <= 0.0)
		{
			vramGb = (double)gpuAdapter.DedicatedMb / 1024.0;
			if (!hasNvidia && !hasRadeon)
			{
				gpuName = gpuAdapter.Name;
			}
		}
		rtx30 = Regex.IsMatch(gpuName, "RTX\\s*30\\d\\d");
		if (!hasNvidia && hasRadeon)
		{
			gpuName = radeonName.Replace("AMD ", "");
		}
	}

	private void ProbeGpu()
	{
		string exe = OwnVictoryExe;
		if (exe == null)
		{
			return;
		}
		Thread thread = new Thread(() =>
		{
			string text = RunCapture(exe, "gpu");
			Dictionary<string, string> kv = new Dictionary<string, string>();
			string[] array = text.Split('\n');
			foreach (string text2 in array)
			{
				int num = text2.IndexOf('=');
				if (num > 0)
				{
					kv[text2.Substring(0, num).Trim()] = text2.Substring(num + 1).Trim();
				}
			}
			if (!kv.TryGetValue("gpu_upload_heap", out var v))
			{
				return;
			}
			try
			{
				BeginInvoke((Action)(() =>
				{
					rebarHeap = ((v == "true") ? 1 : 0);
					probeOptions16 = kv.TryGetValue("options16_known", out var value) && value == "true";
					kv.TryGetValue("adapter", out probeAdapter);
					kv.TryGetValue("vendor", out probeVendor);
					probeAdapter = probeAdapter ?? "";
					probeVendor = probeVendor ?? "";
					if (vramGb <= 0.0 && kv.TryGetValue("dedicated_mb", out var value2) && double.TryParse(value2, out var result))
					{
						vramGb = result / 1024.0;
					}
					if (gpuName == "unknown GPU" && probeAdapter.Length > 0)
					{
						gpuName = probeAdapter;
					}
					rebar = rebarHeap == 1;
					UpdateBanner();
					Refresh2();
					LauncherForm launcherForm = this;
					string format = L("Graphics card for VIctory: {0}; Resizable BAR for the game: {1}.", "Placa grafica do VIctory: {0}; Resizable BAR para o jogo: {1}.");
					string arg = probeAdapter;
					string arg2 = ((rebarHeap == 1) ? L("yes", "sim") : (rebarBios ? L("on in the BIOS, not offered by Windows (see Setup)", "ligado na BIOS, o Windows nao o oferece (ve o Preparar)") : L("no", "nao")));
					launcherForm.Log(string.Format(format, arg, arg2));
				}));
			}
			catch (Exception)
			{
			}
		});
		thread.IsBackground = true;
		thread.Start();
	}

	private bool GpuPreferenceOk()
	{
		if (probeAdapter.Length == 0)
		{
			return true;
		}
		if (hasNvidia && probeAdapter.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return false;
		}
		if (!hasNvidia && hasRadeon && !Regex.IsMatch(probeAdapter, "Radeon\\s*(RX|Pro|VII)", RegexOptions.IgnoreCase))
		{
			return false;
		}
		return true;
	}

	private bool SetGpuPreference()
	{
		try
		{
			using (RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\DirectX\\UserGpuPreferences"))
			{
				foreach (string item in OwnExeCandidates.Concat(new string[3]
				{
					Path.Combine(AmdEngineDir, "target", "diag", "victory.exe"),
					Path.Combine(BuildTarget(AmdEngineDir), "diag", "victory.exe"),
					Path.Combine(AmdEngineDir, "victory.exe")
				}))
				{
					registryKey.SetValue(item, "GpuPreference=2;");
				}
			}
			Log(L("Windows now gives victory.exe the high-performance graphics card (restart the game to apply).", "O Windows da agora ao victory.exe a placa grafica de alto desempenho (reinicia o jogo para aplicar)."));
			return true;
		}
		catch (Exception ex)
		{
			Log("GPU preference: " + ex.Message);
			return false;
		}
	}

	private bool GpuPreferenceSet()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\DirectX\\UserGpuPreferences");
			string victoryExe = VictoryExe;
			if (registryKey == null || victoryExe == null)
			{
				return false;
			}
			return registryKey.GetValue(victoryExe) is string text && text.Contains("GpuPreference=2");
		}
		catch (Exception)
		{
			return false;
		}
	}

	private Dictionary<string, string> AmdEnvironment()
	{
		Dictionary<string, string> e = new Dictionary<string, string>();
		Action<string, string> action = (string k, string v) =>
		{
			e[k] = v;
		};
		action("VICTORY_GAME_DIR", gameBox.Text.Trim());
		action("VICTORY_CACHE_DIR", Path.Combine(dataRoot, "cache-amd"));
		action("VICTORY_DATA_DIR", dataDir);
		action("VICTORY_LOG", "warn");
		action("VICTORY_PIPELINE_BOOK", "1");
		action("VICTORY_PORT_CONSTANTS", "1");
		action("VICTORY_PORT_DESCRIPTORS", "1");
		action("VICTORY_PORT_CBV", "1");
		action("VICTORY_PORT_GUARD", "1");
		action("VICTORY_PORT_JOURNAL", "1");
		int num = Math.Min(DisplayHeight, 1080);
		int num2 = num * 16 / 9;
		action("VICTORY_OUTPUT", num2 + "x" + num);
		action("VICTORY_WINDOW_MAX", num2 + "x" + num);
		action("VICTORY_KBM_GTA", "1");
		action("VICTORY_WAVE_WIDE", "1");
		if (fps60Box.Checked && !Selected.UltraLow)
		{
			action("VICTORY_FRAME_INTERVAL_MS", "16.667");
		}
		if (panelBox.Checked)
		{
			action("VICTORY_PANEL", "1");
		}
		if (borderlessBox.Checked)
		{
			action("VICTORY_BORDERLESS", "1");
			action("VICTORY_BORDERLESS_DPI", "0");
		}
		Mode selected = Selected;
		string text = EffectiveScale(selected.Custom ? custom.Scale : selected.Scale);
		if (text != null && text != "1.0")
		{
			action("VICTORY_RENDER_SCALE", text);
			action("VICTORY_OWN_UPSCALER", "1");
		}
		return e;
	}

	private void FillFgBox()
	{
		if (fgBox != null)
		{
			fgKeys.Clear();
			fgBox.Items.Clear();
			Action<string, string> action = (string key, string text) =>
			{
				fgKeys.Add(key);
				fgBox.Items.Add(text);
			};
			action("auto", L("Automatic (the best this card has)", "Automatico (o melhor desta placa)", "Automatique (le meilleur de cette carte)"));
			if (canSmooth)
			{
				action("smooth", L("NVIDIA Smooth Motion (RTX 40/50)", "NVIDIA Smooth Motion (RTX 40/50)", "NVIDIA Smooth Motion (RTX 40/50)"));
			}
			if (rtx30 && File.Exists(NvSmoothDll))
			{
				action("nvsmooth30", L("NVSmooth30 (RTX 30, experimental)", "NVSmooth30 (RTX 30, experimental)", "NVSmooth30 (RTX 30, expérimental)"));
			}
			if (!UseAmdEngine && !integratedOnly)
			{
				action("vi", L("VIctory 2X (any card, experimental)", "VIctory 2X (qualquer placa, experimental)", "VIctory 2X (toute carte, expérimental)"));
			}
			int num = fgKeys.IndexOf(FgChoice);
			fgBox.SelectedIndex = ((num >= 0) ? num : 0);
		}
	}

	private void PickFg()
	{
		if (fgBox == null || fgBox.SelectedIndex < 0 || fgBox.SelectedIndex >= fgKeys.Count)
		{
			return;
		}
		string text = fgKeys[fgBox.SelectedIndex];
		if (!(text == FgChoice))
		{
			WriteSetting("fg_engine", text);
			rtxFrameGen = canSmooth && (text == "auto" || text == "smooth");
			if (smoothBox is OptionCard)
			{
				((OptionCard)smoothBox).Line = FrameGenLine();
			}
			smoothBox.Invalidate();
			UpdateBanner();
			Refresh2();
			Log(L("Frame generation: ", "Geracao de frames: ", "Génération d'images : ") + fgBox.SelectedItem);
		}
	}

	private string GpuKind()
	{
		GpuAdapter gpuAdapter = null;
		foreach (GpuAdapter adapter in adapters)
		{
			if (gpuAdapter == null || (gpuAdapter.Integrated && !adapter.Integrated) || (gpuAdapter.Integrated == adapter.Integrated && adapter.DedicatedMb > gpuAdapter.DedicatedMb))
			{
				gpuAdapter = adapter;
			}
		}
		double num = ((gpuAdapter != null) ? Math.Max(vramGb, (double)gpuAdapter.DedicatedMb / 1024.0) : vramGb);
		if (!integratedOnly)
		{
			return string.Format(L("dedicated, {0:0.0} GB of its own", "dedicada, {0:0.0} GB proprios", "dédiée, {0:0.0} Go à elle"), num);
		}
		return string.Format(L("integrated, {0:0.0} GB of its own (shares RAM)", "integrada, {0:0.0} GB proprios (partilha a RAM)", "intégrée, {0:0.0} Go à elle (partage la RAM)"), num);
	}

	private string FrameGenLine()
	{
		if (rtxFrameGen)
		{
			return L("NVIDIA Smooth Motion (RTX 40/50, set up for you)", "NVIDIA Smooth Motion (RTX 40/50, configurado por ti)");
		}
		if (NvSmoothHere)
		{
			return L("NVSmooth30 for RTX 30 (experimental)", "NVSmooth30 para RTX 30 (experimental)");
		}
		if (OwnFrameGen)
		{
			return L("VI frame generation: any card, a frame between each two (experimental)", "Geracao de frames VI: qualquer placa, um frame entre cada dois (experimental)");
		}
		return L("Not in the AMD engine yet (pick the edupode engine on Hardware & ID to try it)", "Ainda nao no motor AMD (escolhe o motor edupode em Hardware e ID para experimentar)");
	}

	private string FrameGenChip()
	{
		if (rtxFrameGen)
		{
			return "Smooth Motion " + ((smoothState == "on") ? L("On", "ligado") : L("ready", "pronto"));
		}
		if (NvSmoothHere)
		{
			return "NVSmooth30 " + (smoothBox.Checked ? L("On", "ligado") : L("off", "desligado"));
		}
		if (OwnFrameGen)
		{
			return L("VI frame gen ", "Geracao VI ") + (smoothBox.Checked ? L("On", "ligada") : L("off", "desligada"));
		}
		return L("Frame gen n/a", "Geracao de frames n/d");
	}

	private void ApplyFrameGen()
	{
		if (canSmooth && !rtxFrameGen && smoothState == "on")
		{
			ApplySmooth(on: false, loud: false);
		}
		if (rtxFrameGen)
		{
			if (smoothBox.Checked && smoothState != "on")
			{
				ApplySmooth(on: true, loud: false);
			}
			else if (!smoothBox.Checked && smoothState == "on")
			{
				ApplySmooth(on: false, loud: false);
			}
		}
		else
		{
			if (!rtx30 || !File.Exists(NvSmoothDll) || VictoryExe == null)
			{
				return;
			}
			string destFileName = Path.Combine(Path.GetDirectoryName(VictoryExe), "version.dll");
			try
			{
				if (smoothBox.Checked)
				{
					File.Copy(NvSmoothDll, destFileName, overwrite: true);
					if (smoothState != "on")
					{
						ApplySmooth(on: true, loud: false);
					}
					Log(L("NVSmooth30 (experimental) is on for this session.", "NVSmooth30 (experimental) ligado nesta sessao."));
				}
				else
				{
					RemoveNvSmooth();
				}
			}
			catch (Exception ex)
			{
				Log("NVSmooth30: " + ex.Message);
			}
		}
	}

	private void RemoveNvSmooth()
	{
		try
		{
			if (VictoryExe != null)
			{
				string path = Path.Combine(Path.GetDirectoryName(VictoryExe), "version.dll");
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private static string Wmi(string query, string field)
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher(query);
			foreach (ManagementObject item in managementObjectSearcher.Get())
			{
				object obj = item[field];
				if (obj != null && obj.ToString().Trim().Length > 0)
				{
					return obj.ToString().Trim();
				}
			}
		}
		catch (Exception)
		{
		}
		return "";
	}

	private static string WmiIn(string scope, string query, string field)
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query));
			foreach (ManagementObject item in managementObjectSearcher.Get())
			{
				object obj = item[field];
				if (obj != null)
				{
					return obj.ToString().Trim();
				}
			}
		}
		catch (Exception)
		{
		}
		return "";
	}

	private HardwareInfo ReadHardware()
	{
		HardwareInfo hardwareInfo = new HardwareInfo();
		string text = Wmi("SELECT UUID FROM Win32_ComputerSystemProduct", "UUID");
		string text2 = Wmi("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber");
		string text3 = Wmi("SELECT ProcessorId FROM Win32_Processor", "ProcessorId");
		hardwareInfo.Id = MakeId(text + "|" + text2 + "|" + text3);
		hardwareInfo.Gpu = ((hasNvidia || !hasRadeon) ? gpuName : radeonName);
		ref string gpuVendor = ref hardwareInfo.GpuVendor;
		string text4;
		if (hasNvidia)
		{
			text4 = "NVIDIA";
		}
		else
		{
			text4 = (hasRadeon ? "AMD" : probeVendor);
		}
		gpuVendor = text4;
		hardwareInfo.Driver = driverVersion;
		hardwareInfo.VramMb = (int)(vramGb * 1024.0);
		hardwareInfo.RebarBios = rebarBios;
		hardwareInfo.RebarHeap = rebarHeap;
		hardwareInfo.Cpu = Wmi("SELECT Name FROM Win32_Processor", "Name");
		if (int.TryParse(Wmi("SELECT NumberOfCores FROM Win32_Processor", "NumberOfCores"), out var result))
		{
			hardwareInfo.Cores = result;
		}
		if (int.TryParse(Wmi("SELECT NumberOfLogicalProcessors FROM Win32_Processor", "NumberOfLogicalProcessors"), out result))
		{
			hardwareInfo.Threads = result;
		}
		hardwareInfo.RamGb = (int)Math.Round(ramGb);
		hardwareInfo.BoardMaker = Wmi("SELECT Manufacturer FROM Win32_BaseBoard", "Manufacturer");
		hardwareInfo.Board = Wmi("SELECT Product FROM Win32_BaseBoard", "Product");
		hardwareInfo.Os = Wmi("SELECT Caption FROM Win32_OperatingSystem", "Caption") + " " + Wmi("SELECT BuildNumber FROM Win32_OperatingSystem", "BuildNumber");
		string text5 = WmiIn("\\\\.\\root\\Microsoft\\Windows\\Storage", "SELECT MediaType FROM MSFT_PhysicalDisk WHERE DeviceId='0'", "MediaType");
		ref string disk = ref hardwareInfo.Disk;
		string text6;
		if (text5 == "4")
		{
			text6 = "SSD";
		}
		else
		{
			text6 = ((text5 == "3") ? "HDD" : "unknown");
		}
		disk = text6;
		Wmi("SELECT ChassisTypes FROM Win32_SystemEnclosure", "ChassisTypes");
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
			foreach (ManagementObject item in managementObjectSearcher.Get())
			{
				if (item["ChassisTypes"] is ushort[] source && source.Any((ushort t) => t == 8 || t == 9 || t == 10 || t == 14 || t == 31 || t == 32))
				{
					hardwareInfo.Laptop = true;
				}
			}
		}
		catch (Exception)
		{
		}
		return hardwareInfo;
	}

	private static string MakeId(string parts)
	{
		byte[] array;
		using (SHA256 sHA = SHA256.Create())
		{
			array = sHA.ComputeHash(Encoding.UTF8.GetBytes(parts + "|cold-project-vi"));
		}
		ulong num = 0uL;
		for (int i = 0; i < 5; i++)
		{
			num = (num << 8) | array[i];
		}
		StringBuilder stringBuilder = new StringBuilder("VI-");
		for (int num2 = 7; num2 >= 0; num2--)
		{
			stringBuilder.Append("ABCDEFGHJKLMNPQRSTUVWXYZ23456789"[(int)((num >> num2 * 5) & 0x1F)]);
			if (num2 == 4)
			{
				stringBuilder.Append('-');
			}
		}
		return stringBuilder.ToString();
	}

	private static string Json(string s)
	{
		StringBuilder stringBuilder = new StringBuilder("\"");
		string text = s ?? "";
		foreach (char c in text)
		{
			if (c == '"' || c == '\\')
			{
				stringBuilder.Append('\\').Append(c);
			}
			else if (c < ' ')
			{
				stringBuilder.AppendFormat("\\u{0:x4}", (int)c);
			}
			else
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.Append('"').ToString();
	}

	private static string HardwareJson(HardwareInfo h)
	{
		return "{\n  \"schema\": 1,\n  \"id\": " + Json(h.Id) + ",\n  \"gpu\": " + Json(h.Gpu) + ",\n  \"gpu_vendor\": " + Json(h.GpuVendor) + ",\n  \"gpu_driver\": " + Json(h.Driver) + ",\n  \"vram_mb\": " + h.VramMb + ",\n  \"rebar_bios\": " + (h.RebarBios ? "true" : "false") + ",\n  \"rebar_d3d12\": " + ((h.RebarHeap == 1) ? "true" : ((h.RebarHeap == 0) ? "false" : "null")) + ",\n  \"cpu\": " + Json(h.Cpu) + ",\n  \"cpu_cores\": " + h.Cores + ",\n  \"cpu_threads\": " + h.Threads + ",\n  \"ram_gb\": " + h.RamGb + ",\n  \"board_maker\": " + Json(h.BoardMaker) + ",\n  \"board\": " + Json(h.Board) + ",\n  \"os\": " + Json(h.Os) + ",\n  \"disk\": " + Json(h.Disk) + ",\n  \"laptop\": " + (h.Laptop ? "true" : "false") + "\n}";
	}

	private string ShareSecret()
	{
		string path = Path.Combine(Path.GetDirectoryName(dataRoot), "hardware-secret.txt");
		try
		{
			if (File.Exists(path))
			{
				string text = File.ReadAllText(path).Trim();
				if (text.Length == 64)
				{
					return text;
				}
			}
			byte[] array = new byte[32];
			using (RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create())
			{
				randomNumberGenerator.GetBytes(array);
			}
			string text2 = string.Concat(array.Select((byte b) => b.ToString("x2")).ToArray());
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, text2);
			return text2;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private void StartHardwareShare()
	{
		if (ReadSetting("share") != "1" || "https://vi-hardware.edupodept.workers.dev/v1/hw".Length == 0)
		{
			return;
		}
		string text = ReadSetting("share_sent");
		if (text == DateTime.UtcNow.ToString("yyyy-MM-dd"))
		{
			return;
		}
		Thread thread = new Thread(() =>
		{
			try
			{
				Thread.Sleep(15000);
				HardwareInfo h = ReadHardware();
				string text2 = ShareSecret();
				if (text2 == null)
				{
					return;
				}
				ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
				HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create("https://vi-hardware.edupodept.workers.dev/v1/hw");
				httpWebRequest.Method = "POST";
				httpWebRequest.ContentType = "application/json";
				httpWebRequest.UserAgent = "Julias-Launcher";
				httpWebRequest.Timeout = 15000;
				httpWebRequest.Headers["X-VI-Secret"] = text2;
				byte[] bytes = Encoding.UTF8.GetBytes(HardwareJson(h));
				httpWebRequest.ContentLength = bytes.Length;
				using (Stream stream = httpWebRequest.GetRequestStream())
				{
					stream.Write(bytes, 0, bytes.Length);
				}
				using HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
				if ((int)httpWebResponse.StatusCode / 100 == 2)
				{
					WriteSetting("share_sent", DateTime.UtcNow.ToString("yyyy-MM-dd"));
				}
			}
			catch (Exception)
			{
			}
		});
		thread.IsBackground = true;
		thread.Start();
	}

	private static int FitText(Label l, int top)
	{
		int val = TextRenderer.MeasureText(l.Text ?? "", l.Font, new Size(l.Width, 0), TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak).Height + 8;
		l.Height = Math.Max(60, val);
		return top + l.Height;
	}

	private void AskToShare()
	{
		if ("https://vi-hardware.edupodept.workers.dev/v1/hw".Length == 0 || ReadSetting("share") == "1" || ReadSetting("share_ask") == "never")
		{
			return;
		}
		Form f = new Form
		{
			Text = L("Help the game run on more PCs", "Ajuda o jogo a correr em mais PCs"),
			ClientSize = new Size(660, 520),
			BackColor = Theme.Back,
			ForeColor = Theme.Text,
			Font = Theme.Ui(9.75f),
			StartPosition = FormStartPosition.CenterParent,
			FormBorderStyle = FormBorderStyle.FixedDialog,
			MaximizeBox = false,
			MinimizeBox = false,
			Icon = Icon
		};
		f.Controls.Add(new Heading(L("Share your hardware?", "Partilhar o teu hardware?"))
		{
			Location = new Point(20, 16),
			Width = 620
		});
		Label label = new Label();
		label.Location = new Point(20, 50);
		label.Size = new Size(620, 390);
		label.Font = f.Font;
		label.ForeColor = Theme.Text;
		label.BackColor = Theme.Back;
		Label label2 = label;
		Label label3 = label2;
		string[] value;
		if (Pt)
		{
			value = new string[20]
			{
				"Podes deixar o Julia's Launcher enviar o hardware do teu PC ao edupode?", "", "PORQUE AJUDA", "   Vemos em que placas graficas, processadores e quantidades de memoria as pessoas jogam de verdade.", "   Isso decide que predefinicoes vem primeiro, que correcoes se fazem a seguir (por exemplo o Ultra baixo", "   para PCs de 16 GB, o suporte AMD) e em que placas se testa a geracao de frames.", "", "A LISTA", "   Cada PC que partilha e uma linha, sem nome. So o edupode ve a lista.", "   Uma pagina publica mostra so as medias (quantos PCs, RAM e VRAM medias, as placas mais comuns),",
				"   atualizada de 6 em 6 horas: docs/HARDWARE-STATS.md no GitHub.", "", "O QUE E ENVIADO", "   Placa grafica e driver, VRAM, Resizable BAR, processador, RAM, modelo da placa-mae,", "   versao do Windows, tipo de disco, se e portatil, e o teu ID VI. No maximo uma vez por dia.", "", "NUNCA E ENVIADO", "   Logs, definicoes, caminhos de ficheiros, o teu nome, o teu IP.", "", "Podes desligar quando quiseres em Hardware e ID."
			};
		}
		else
		{
			value = (Fr ? new string[20]
			{
				"Julia's Launcher peut-il envoyer le matériel de ton PC à edupode ?", "", "POURQUOI ÇA AIDE", "   On voit sur quelles cartes graphiques, quels processeurs et quelles quantités de mémoire les gens jouent vraiment.", "   Ça décide quels préréglages passent en premier, quels correctifs viennent ensuite (par exemple Ultra bas pour", "   les PC de 16 Go, le support AMD) et sur quelles cartes la génération d'images est testée.", "", "LA LISTE", "   Chaque PC qui partage est une ligne, sans nom. Seul edupode voit la liste.", "   Une page publique ne montre que les moyennes (combien de PC, RAM et VRAM moyennes, les cartes les plus",
				"   courantes), mise à jour toutes les 6 heures : docs/HARDWARE-STATS.md sur GitHub.", "", "CE QUI EST ENVOYÉ", "   Carte graphique et pilote, VRAM, Resizable BAR, processeur, RAM, modèle de carte mère,", "   version de Windows, type de disque, portable ou non, et ton ID VI. Au plus une fois par jour.", "", "JAMAIS ENVOYÉ", "   Logs, réglages, chemins de fichiers, ton nom, ton adresse IP.", "", "Tu peux le désactiver à tout moment dans Matériel et ID."
			} : new string[20]
			{
				"Can Julia's Launcher send your PC's hardware to edupode?", "", "WHY IT HELPS", "   We see which graphics cards, processors and amounts of memory people really play on. That decides", "   which presets come first, which fixes come next (for example Ultra Low for 16 GB PCs, AMD support)", "   and which cards frame generation is tested on.", "", "THE LIST", "   Each PC that shares is one line, with no name. Only edupode sees the list.", "   A public page shows only the averages (how many PCs, average RAM and video memory, the most",
				"   common cards), updated every 6 hours: docs/HARDWARE-STATS.md on GitHub.", "", "WHAT IS SENT", "   Graphics card and driver, video memory, Resizable BAR, processor, RAM, motherboard model,", "   Windows version, disk type, laptop or not, and your VI ID. At most once a day.", "", "NEVER SENT", "   Logs, settings, file paths, your name, your IP address.", "", "You can turn it off any time on Hardware & ID."
			});
		}
		label3.Text = string.Join("\r\n", value);
		int num = FitText(label2, 50) + 16;
		f.ClientSize = new Size(660, num + 62);
		f.Controls.Add(label2);
		Pill pill = new Pill();
		pill.Main = true;
		pill.Text = L("Yes, share my hardware", "Sim, partilhar o meu hardware");
		pill.Location = new Point(20, num);
		pill.Size = new Size(250, 42);
		Pill pill2 = pill;
		Pill pill3 = new Pill();
		pill3.Text = L("Not now", "Agora nao");
		pill3.Location = new Point(280, num + 4);
		pill3.Size = new Size(140, 34);
		Pill pill4 = pill3;
		Pill pill5 = new Pill();
		pill5.Text = L("Don't ask again", "Nao perguntar mais");
		pill5.Location = new Point(430, num + 4);
		pill5.Size = new Size(190, 34);
		Pill pill6 = pill5;
		pill2.Click += (object s, EventArgs e) =>
		{
			if (shareBox != null && !shareBox.Checked)
			{
				shareBox.Checked = true;
			}
			else
			{
				WriteSetting("share", "1");
				WriteSetting("share_sent", "");
				StartHardwareShare();
			}
			Log(L("Hardware sharing is on. Thank you!", "Partilha de hardware ligada. Obrigado!"));
			f.Close();
		};
		pill4.Click += (object s, EventArgs e) =>
		{
			f.Close();
		};
		pill6.Click += (object s, EventArgs e) =>
		{
			WriteSetting("share_ask", "never");
			f.Close();
		};
		f.Controls.Add(pill2);
		f.Controls.Add(pill4);
		f.Controls.Add(pill6);
		f.CancelButton = pill4;
		f.ShowDialog(this);
		f.Dispose();
	}

	private void ShowNotices()
	{
		if (OwnFrameGen && ReadSetting("notice_fg") != "1")
		{
			WriteSetting("notice_fg", "1");
			string[] value;
			if (Pt)
			{
				value = new string[14]
				{
					"A tua placa (" + gpuName + ") nao tem o NVIDIA Smooth Motion. Agora o Julia's Launcher tem a sua",
					"propria geracao de frames: cria um frame entre cada dois do jogo, so a partir das imagens.",
					"",
					"O QUE MUDA",
					"   O movimento fica mais fluido. Os FPS reais do jogo nao sobem, e ha meio frame a mais de atraso.",
					"",
					"AINDA NINGUEM A TESTOU NO JOGO: es dos primeiros.",
					"   1.  Liga 'Geracao de frames' nas Opcoes de arranque (ou carrega em 'Ligar agora').",
					"   2.  Joga uns minutos com o F3 ligado.",
					"   3.  Conta no Discord, no canal \"Send bugs here please\": a tua placa, se ficou mais fluido,",
					"        se ha tremeliques ou rastos, e se o jogo fechou. O 'Enviar erros no Discord' tem a opcao",
					"        'Teste da geracao de frames' e junta os logs.",
					"",
					"Se correr mal, desliga a opcao e fica tudo como antes."
				};
			}
			else
			{
				value = (Fr ? new string[14]
				{
					"Ta carte (" + gpuName + ") n'a pas NVIDIA Smooth Motion. Julia's Launcher a maintenant sa propre",
					"génération d'images : il crée une image entre chaque paire d'images du jeu, à partir des images seules.",
					"",
					"CE QUI CHANGE",
					"   Le mouvement paraît plus fluide. Les vrais FPS du jeu n'augmentent pas, et il y a une demi-image de retard en plus.",
					"",
					"PERSONNE NE L'A ENCORE TESTÉE EN JEU : tu es parmi les premiers.",
					"   1.  Active 'Génération d'images' dans Options de lancement (ou appuie sur 'Activer maintenant').",
					"   2.  Joue quelques minutes avec F3 activé.",
					"   3.  Raconte-nous sur Discord, dans le salon \"Send bugs here please\" : ta carte, si c'est plus fluide,",
					"        s'il y a du scintillement ou des traînées, et si le jeu s'est fermé. 'Envoyer les erreurs sur Discord'",
					"        a une case 'Test de la génération d'images' et ajoute les logs.",
					"",
					"Si ça se passe mal, désactive l'option et tout redevient comme avant."
				} : new string[14]
				{
					"Your card (" + gpuName + ") has no NVIDIA Smooth Motion. Julia's Launcher now has its own",
					"frame generation: it makes a frame between each two of the game's, from the pictures alone.",
					"",
					"WHAT CHANGES",
					"   Motion looks smoother. The game's real FPS do not go up, and there is half a frame more delay.",
					"",
					"NOBODY HAS TESTED IT IN THE GAME YET: you are among the first.",
					"   1.  Turn on 'Frame generation' in Launch options (or press 'Turn it on now').",
					"   2.  Play a few minutes with F3 on.",
					"   3.  Tell us on Discord, in the \"Send bugs here please\" channel: your card, whether it looks",
					"        smoother, any flicker or ghosting, and whether the game closed. 'Send errors to Discord'",
					"        has a 'Frame generation test' tick and adds the logs.",
					"",
					"If it goes wrong, turn the option off and everything is as before."
				});
			}
			string body = string.Join("\r\n", value);
			int num = Notice(L("New: frame generation for your card (experimental)", "Novo: geracao de frames para a tua placa (experimental)"), body, L("Turn it on now", "Ligar agora"), L("Open Discord", "Abrir o Discord"));
			if (num == 1)
			{
				smoothBox.Checked = true;
				SaveSettings();
				Log(L("Frame generation is on (experimental). Tell us how it runs on Discord!", "Geracao de frames ligada (experimental). Conta-nos como corre no Discord!"));
			}
			if (num == 2)
			{
				OpenUrl("https://discord.gg/648WEXCAsZ");
			}
		}
		if (!hasRadeon || hasNvidia || !(ReadSetting("notice_amd") != "1"))
		{
			return;
		}
		WriteSetting("notice_amd", "1");
		string[] value2;
		if (Pt)
		{
			value2 = new string[9]
			{
				"Placa AMD detetada (" + gpuName + "). Jogas com o motor edupode, o motor deste projeto e o predefinido",
				"para todas as placas:",
				"   •  VRAM de ~12 GB para 6-8 GB na placa do autor, e ~4 vezes mais texturas na mesma memoria;",
				"   •  sessoes longas e missoes sem crashes.",
				"",
				"Foi afinado numa placa NVIDIA: conta-nos no Discord (\"Send bugs here please\") os FPS e a VRAM na tua Radeon.",
				"",
				"Alternativa para Radeon: o VIctory-Belobug-AMD, o fork do Belobug afinado para AMD (o credito e dele).",
				"Podes trocar quando quiseres em Hardware e ID > Motor."
			};
		}
		else
		{
			value2 = (Fr ? new string[9]
			{
				"Carte AMD détectée (" + gpuName + "). Tu joues avec le moteur edupode, le moteur de ce projet et celui par défaut",
				"pour toutes les cartes :",
				"   •  VRAM de ~12 Go à 6-8 Go sur la carte de l'auteur, et ~4 fois plus de textures dans la même mémoire ;",
				"   •  longues sessions et missions sans plantage.",
				"",
				"Il a été réglé sur une carte NVIDIA : dis-nous sur Discord (\"Send bugs here please\") les FPS et la VRAM sur ta Radeon.",
				"",
				"Alternative pour Radeon : VIctory-Belobug-AMD, le fork de Belobug réglé pour AMD (le mérite lui revient).",
				"Tu peux changer à tout moment dans Matériel et ID > Moteur."
			} : new string[9]
			{
				"AMD card found (" + gpuName + "). You play with the edupode engine, this project's engine and the default",
				"for every card:",
				"   •  video memory from ~12 GB down to 6-8 GB on the author's card, ~4 times more textures in the same memory;",
				"   •  long sessions and missions without crashes.",
				"",
				"It was tuned on an NVIDIA card: tell us on Discord (\"Send bugs here please\") the FPS and video memory on your Radeon.",
				"",
				"Alternative for Radeon: VIctory-Belobug-AMD, Belobug's fork tuned for AMD (the credit is his).",
				"You can switch any time in Hardware & ID > Engine."
			});
		}
		string body2 = string.Join("\r\n", value2);
		int num2 = Notice(L("AMD card: you play with the edupode engine", "Placa AMD: jogas com o motor edupode", "Carte AMD : tu joues avec le moteur edupode"), body2, L("Keep edupode", "Ficar com o edupode", "Garder edupode"), L("Try Belobug's instead", "Experimentar o do Belobug", "Essayer celui de Belobug"));
		if (num2 == 2)
		{
			WriteSetting("engine", "amd");
			if (engineBox != null)
			{
				engineBox.SelectedIndex = 1;
			}
			UpdateBanner();
			Refresh2();
			if (VictoryExe == null)
			{
				Show(4);
				Build();
			}
		}
	}

	private int Notice(string title, string body, string first, string second)
	{
		int answer = 0;
		Form f = new Form
		{
			Text = title,
			ClientSize = new Size(700, 470),
			BackColor = Theme.Back,
			ForeColor = Theme.Text,
			Font = Theme.Ui(9.75f),
			StartPosition = FormStartPosition.CenterParent,
			FormBorderStyle = FormBorderStyle.FixedDialog,
			MaximizeBox = false,
			MinimizeBox = false,
			Icon = Icon
		};
		f.Controls.Add(new Heading(title)
		{
			Location = new Point(20, 16),
			Width = 660
		});
		Label label = new Label();
		label.Font = f.Font;
		label.Location = new Point(20, 50);
		label.Size = new Size(660, 340);
		label.Text = body;
		label.ForeColor = Theme.Text;
		label.BackColor = Theme.Back;
		Label label2 = label;
		int num = FitText(label2, 50) + 16;
		f.ClientSize = new Size(700, num + 62);
		f.Controls.Add(label2);
		Pill pill = new Pill();
		pill.Main = true;
		pill.Text = first;
		pill.Location = new Point(20, num);
		pill.Size = new Size(250, 42);
		Pill pill2 = pill;
		Pill pill3 = new Pill();
		pill3.Text = second;
		pill3.Location = new Point(280, num + 4);
		pill3.Size = new Size(220, 34);
		Pill pill4 = pill3;
		Pill pill5 = new Pill();
		pill5.Text = L("Later", "Mais tarde");
		pill5.Location = new Point(510, num + 4);
		pill5.Size = new Size(170, 34);
		Pill pill6 = pill5;
		pill2.Click += (object s, EventArgs e) =>
		{
			answer = 1;
			f.Close();
		};
		pill4.Click += (object s, EventArgs e) =>
		{
			answer = 2;
			f.Close();
		};
		pill6.Click += (object s, EventArgs e) =>
		{
			f.Close();
		};
		f.Controls.Add(pill2);
		f.Controls.Add(pill4);
		f.Controls.Add(pill6);
		f.CancelButton = pill6;
		f.ShowDialog(this);
		f.Dispose();
		return answer;
	}

	private Panel HardwarePage()
	{
		Panel panel = Page();
		int num = PageTop(panel, L("Hardware & ID", "Hardware e ID", "Matériel et ID"), L("Hardware", "Hardware", "Matériel"), L("and your ID.", "e o teu ID.", "et ton ID."), L("Your ID, what this PC is made of, and which engine runs the game.", "O teu ID, de que e feito este PC, e que motor corre o jogo.", "Ton ID, de quoi ce PC est fait, et quel moteur lance le jeu."));
		idLabel = new Label
		{
			Location = new Point(40, num),
			Size = new Size(300, 40),
			Font = Theme.Mono(18f),
			ForeColor = Theme.Cyan,
			BackColor = Theme.Back,
			Text = "VI-····-····"
		};
		panel.Controls.Add(idLabel);
		Pill pill = new Pill();
		pill.Text = L("Copy ID", "Copiar ID");
		pill.Location = new Point(350, num + 3);
		pill.Size = new Size(120, 34);
		Pill pill2 = pill;
		pill2.Click += (object s, EventArgs e) =>
		{
			if (hw != null)
			{
				Clipboard.SetText(hw.Id);
				Log(L("ID copied: ", "ID copiado: ") + hw.Id);
			}
		};
		panel.Controls.Add(pill2);
		num += 44;
		panel.Controls.Add(new Label
		{
			Location = new Point(40, num),
			Size = new Size(900, 38),
			ForeColor = Theme.Dim,
			BackColor = Theme.Back,
			Font = Theme.Ui(9f),
			Text = L("Made from parts of your PC (the motherboard and the processor): it stays the same after reinstalling Windows. Put it in your bug reports.", "Feito a partir de pecas do teu PC (placa-mae e processador): fica igual depois de reinstalar o Windows. Poe-no nos teus relatorios de erros.")
		});
		num += 44;
		panel.Controls.Add(new Heading(L("Hardware", "Hardware"))
		{
			Location = new Point(40, num),
			Width = 900
		});
		num += 32;
		hwText = new InfoCard
		{
			Location = new Point(40, num),
			Size = new Size(900, 56),
			Empty = L("Reading your hardware...", "A ler o teu hardware...", "Lecture de ton matériel...")
		};
		panel.Controls.Add(hwText);
		num += hwText.Height + 12;
		panel.Controls.Add(new Heading(L("Engine", "Motor"))
		{
			Location = new Point(40, num),
			Width = 900
		});
		num += 32;
		engineBox = new ComboBox
		{
			Location = new Point(40, num),
			Width = 420,
			DropDownStyle = ComboBoxStyle.DropDownList,
			FlatStyle = FlatStyle.Flat,
			BackColor = Theme.Card,
			ForeColor = Theme.Text
		};
		engineBox.Items.AddRange(new object[2]
		{
			L("★ edupode (recommended, every card: least video memory, most stable)", "★ edupode (recomendado, todas as placas: menos VRAM, mais estavel)"),
			L("AMD alternative · VIctory-Belobug-AMD by Belobug", "Alternativa AMD · VIctory-Belobug-AMD do Belobug")
		});
		engineBox.SelectedIndex = ((EngineChoice == "amd") ? 1 : 0);
		engineBox.SelectedIndexChanged += (object s, EventArgs e) =>
		{
			WriteSetting("engine", (engineBox.SelectedIndex == 1) ? "amd" : "edupode");
			Log(L("Engine: ", "Motor: ") + EngineName);
			UpdateBanner();
			Refresh2();
		};
		engineBox.Visible = false;
		panel.Controls.Add(engineBox);
		Pill hwStd = new Pill
		{
			Text = L("edupode · recommended", "edupode · recomendado", "edupode · recommandé"),
			Location = new Point(40, num),
			Size = new Size(230, 36)
		};
		Pill hwAmd = new Pill
		{
			Text = L("Belobug AMD · for Radeon", "Belobug AMD · para Radeon", "Belobug AMD · pour Radeon"),
			Location = new Point(280, num),
			Size = new Size(230, 36)
		};
		hwStd.Click += (object s, EventArgs e) =>
		{
			if (UseAmdEngine)
			{
				engineBox.SelectedIndex = 0;
			}
		};
		hwAmd.Click += (object s, EventArgs e) =>
		{
			if (!UseAmdEngine)
			{
				engineBox.SelectedIndex = 1;
			}
		};
		showEnginePills = (Action)Delegate.Combine(showEnginePills, (Action)(() =>
		{
			hwStd.Main = !UseAmdEngine;
			hwAmd.Main = UseAmdEngine;
			hwStd.Invalidate();
			hwAmd.Invalidate();
		}));
		hwStd.Main = !UseAmdEngine;
		hwAmd.Main = UseAmdEngine;
		panel.Controls.Add(hwStd);
		panel.Controls.Add(hwAmd);
		num += 46;
		panel.Controls.Add(new Label
		{
			Location = new Point(40, num),
			Size = new Size(900, 52),
			ForeColor = Theme.Dim,
			BackColor = Theme.Back,
			Font = Theme.Ui(9f),
			Text = L("Every card plays with the edupode engine: video memory from ~12 GB down to 6-8 GB, ~4x more textures in it, the most stable. For Radeon cards there is also VIctory-Belobug-AMD, Belobug's fork tuned for Radeon (github.com/Belobug/VIctory-Belobug-AMD, GPL-3.0; all credit for it is Belobug's), which Setup builds once.", "Todas as placas jogam com o motor edupode: VRAM de ~12 GB para 6-8 GB, ~4x mais texturas, o mais estavel. Para placas Radeon ha tambem o VIctory-Belobug-AMD, o fork do Belobug afinado para Radeon (github.com/Belobug/VIctory-Belobug-AMD, GPL-3.0; todo o credito e do Belobug), que o Preparar compila uma vez.")
		});
		num += 60;
		num = SavesSection(panel, 40, num, 900);
		num = ReferencePcSection(panel, 40, num, 900);
		num = PlayersPcsSection(panel, 40, num, 900);
		panel.Controls.Add(new Heading(L("Hardware sharing (only hardware)", "Partilha de hardware (so hardware)"))
		{
			Location = new Point(40, num),
			Width = 900
		});
		num += 32;
		shareBox = new OptionCard
		{
			Text = L("Share my hardware with edupode", "Partilhar o meu hardware com o edupode"),
			Line = L("Off by default. Only the list below; never logs, options, paths, names or your IP.", "Desligado por defeito. So a lista em baixo; nunca logs, opcoes, caminhos, nomes nem o teu IP."),
			Location = new Point(40, num),
			Size = new Size(900, 58),
			Checked = (ReadSetting("share") == "1")
		};
		shareBox.CheckedChanged += (object s, EventArgs e) =>
		{
			WriteSetting("share", shareBox.Checked ? "1" : "0");
			if (shareBox.Checked)
			{
				WriteSetting("share_sent", "");
				StartHardwareShare();
			}
			RefreshHardware();
		};
		panel.Controls.Add(shareBox);
		num += 66;
		num = SessionLogsSection(panel, 40, num, 900);
		shareStatus = new Label
		{
			Location = new Point(40, num),
			Size = new Size(900, 40),
			ForeColor = Theme.Dim,
			BackColor = Theme.Back,
			Font = Theme.Ui(9f)
		};
		panel.Controls.Add(shareStatus);
		num += 44;
		panel.Controls.Add(Small(L("Exactly what is sent:", "Exatamente o que e enviado:"), 40, num, 900));
		num += 22;
		CardPanel cardPanel = new CardPanel();
		cardPanel.Location = new Point(40, num);
		cardPanel.Size = new Size(900, 300);
		CardPanel cardPanel2 = cardPanel;
		hwJson = new TextBox
		{
			Location = new Point(14, 12),
			Size = new Size(876, 276),
			Multiline = true,
			ReadOnly = true,
			BorderStyle = BorderStyle.None,
			BackColor = Theme.Card,
			ForeColor = Theme.Soft,
			Font = Theme.Mono(8.5f),
			ScrollBars = ScrollBars.Vertical,
			Anchor = (AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right)
		};
		cardPanel2.Controls.Add(hwJson);
		panel.Controls.Add(cardPanel2);
		panel.Controls.Add(new Label
		{
			Location = new Point(40, num + 310),
			Size = new Size(10, 10),
			BackColor = Theme.Back
		});
		return panel;
	}

	private void RefreshHardware()
	{
		Thread thread = new Thread(() =>
		{
			HardwareInfo h = null;
			try
			{
				h = ReadHardware();
			}
			catch (Exception)
			{
			}
			try
			{
				BeginInvoke((Action)(() =>
				{
					if (h == null)
					{
						hwText.Empty = L("The hardware could not be read.", "Nao foi possivel ler o hardware.", "Le matériel n'a pas pu être lu.");
						hwText.Invalidate();
					}
					else
					{
						hw = h;
						idLabel.Text = h.Id;
						hwText.Text = string.Join("\r\n", L("Graphics card ", "Placa grafica ") + ": " + h.Gpu + ((h.Driver.Length > 0) ? ("  (driver " + h.Driver + ")") : "") + "  ·  " + GpuKind(), "Resizable BAR : " + RebarText() + ((probeAdapter.Length > 0) ? ("  ·  " + L("VIctory runs on ", "o VIctory corre na ") + probeAdapter) : ""), L("Processor     ", "Processador   ") + ": " + h.Cpu + "  ·  " + h.Cores + L(" cores, ", " nucleos, ") + h.Threads + " threads", "RAM           : " + h.RamGb + " GB", L("Motherboard   ", "Placa-mae     ") + ": " + h.BoardMaker + " " + h.Board, "Windows       : " + h.Os, L("System disk   ", "Disco         ") + ": " + h.Disk + (h.Laptop ? L("  ·  laptop", "  ·  portatil") : ""), L("Engine        ", "Motor         ") + ": " + EngineName);
						GrowTo(hwText, hwText.Fit());
						hwText.Invalidate();
						hwJson.Text = HardwareJson(h).Replace("\n", "\r\n");
						Label label = shareStatus;
						string text = (("https://vi-hardware.edupodept.workers.dev/v1/hw".Length == 0) ? L("The database is not online yet: nothing is sent, even with sharing on.", "A base de dados ainda nao esta online: nada e enviado, mesmo com a partilha ligada.") : ((!shareBox.Checked) ? L("Off: nothing is sent.", "Desligado: nada e enviado.") : L("On: sent at most once a day, to a database only edupode can read. Turn it off any time.", "Ligado: enviado no maximo uma vez por dia, para uma base de dados que so o edupode le. Desliga quando quiseres.")));
						label.Text = text;
					}
				}));
			}
			catch (Exception)
			{
			}
		});
		thread.IsBackground = true;
		thread.Start();
	}

	private void StartUpdates()
	{
		try
		{
			string path = Path.Combine(root, "Julia's Launcher.old.exe");
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception)
		{
		}
		updateTimer = new System.Threading.Timer((object _) =>
		{
			CheckForUpdate(loud: false);
		}, null, 3000, 300000);
		FormClosed += (object s, FormClosedEventArgs e) =>
		{
			try
			{
				updateTimer.Dispose();
			}
			catch (Exception)
			{
			}
		};
	}

	private void CheckForUpdate(bool loud)
	{
		if (Interlocked.Exchange(ref checking, 1) == 1)
		{
			return;
		}
		try
		{
			lastCheck = DateTime.Now;
			if (pending != null)
			{
				TryInstall(now: false);
				return;
			}
			SetUpdateState(L("Checking GitHub...", "A verificar o GitHub..."));
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			if (!NewestSignedRelease(out var manifestUrl, out var signatureUrl))
			{
				manifestUrl = "https://github.com/felicienradio/cold-project-vi/releases/latest/download/manifest.json";
				signatureUrl = "https://github.com/felicienradio/cold-project-vi/releases/latest/download/manifest.sig";
			}
			byte[] array;
			byte[] signature;
			using (WebClient webClient = new WebClient())
			{
				webClient.Headers[HttpRequestHeader.UserAgent] = "Julias-Launcher";
				try
				{
					array = webClient.DownloadData(manifestUrl);
				}
				catch (WebException ex)
				{
					HttpWebResponse httpWebResponse = ex.Response as HttpWebResponse;
					SetUpdateState((httpWebResponse != null && httpWebResponse.StatusCode == HttpStatusCode.NotFound) ? L("Up to date (no release published yet).", "Atualizado (ainda nao ha versoes publicadas).") : L("Could not reach GitHub; trying again in 5 minutes.", "Sem ligacao ao GitHub; nova tentativa daqui a 5 minutos."));
					return;
				}
				signature = Convert.FromBase64String(Encoding.ASCII.GetString(webClient.DownloadData(signatureUrl)).Trim());
			}
			if (!SignedByEdupode(array, signature))
			{
				SetUpdateState(L("An update was refused: its signature is not the project's.", "Um update foi recusado: a assinatura nao e a do projeto."));
				return;
			}
			Manifest m = ParseManifest(Encoding.UTF8.GetString(array));
			if (m == null)
			{
				SetUpdateState(L("An update was refused: its list could not be read.", "Um update foi recusado: a lista nao se le."));
				return;
			}
			if (Updater.Number(m.Version) <= Updater.Number("0.3.16") || m.Version == installedVersion)
			{
				SetUpdateState(string.Format(L("Up to date (v{0}).", "Atualizado (v{0})."), "0.3.16"));
				if (loud)
				{
					Log(L("No update: this is the latest version.", "Sem updates: esta e a versao mais recente."));
				}
				return;
			}
			SetUpdateState(string.Format(L("Downloading v{0}...", "A descarregar a v{0}..."), m.Version));
			string text = Path.Combine(Path.GetDirectoryName(dataRoot), "updates", m.Version);
			Directory.CreateDirectory(text);
			using (WebClient webClient2 = new WebClient())
			{
				webClient2.Headers[HttpRequestHeader.UserAgent] = "Julias-Launcher";
				foreach (UpdateFile file in m.Files)
				{
					string text2 = Path.Combine(text, file.Name.Replace('/', '\\'));
					Directory.CreateDirectory(Path.GetDirectoryName(text2));
					if (!File.Exists(text2) || !(Sha256(text2) == file.Sha256))
					{
						webClient2.DownloadFile(file.Url, text2);
					}
					if (new FileInfo(text2).Length != file.Size || Sha256(text2) != file.Sha256)
					{
						File.Delete(text2);
						SetUpdateState(string.Format(L("v{0} was refused: {1} does not match its SHA-256.", "A v{0} foi recusada: o {1} nao bate com o SHA-256."), m.Version, file.Name));
						return;
					}
				}
			}
			pending = m;
			pendingDir = text;
			try
			{
				BeginInvoke((Action)(() =>
				{
					Log(string.Format(L("Update v{0} downloaded and checked.", "Update v{0} descarregado e verificado."), m.Version));
					RefreshUpdates();
				}));
			}
			catch (Exception)
			{
			}
			TryInstall(now: false);
		}
		catch (Exception ex3)
		{
			SetUpdateState(L("Update check failed: ", "A verificacao falhou: ") + ex3.Message);
		}
		finally
		{
			Interlocked.Exchange(ref checking, 0);
		}
	}

	private void TryInstall(bool now)
	{
		if (pending == null)
		{
			return;
		}
		try
		{
			BeginInvoke((Action)(() =>
			{
				if (pending != null)
				{
					bool flag = ReadSetting("autoupdate") != "0";
					if (!now && !flag)
					{
						SetUpdateState(string.Format(L("v{0} is ready: press 'Install now'.", "A v{0} esta pronta: carrega em 'Instalar agora'."), pending.Version));
					}
					else if (State() != GameState.None || busyNow || setupBusy)
					{
						SetUpdateState(string.Format(L("v{0} is ready: it installs when the game closes.", "A v{0} esta pronta: instala-se quando o jogo fechar."), pending.Version));
					}
					else
					{
						Install();
					}
				}
			}));
		}
		catch (Exception)
		{
		}
	}

	private static bool VictoryRunning()
	{
		try
		{
			Process[] processesByName = Process.GetProcessesByName("victory");
			Process[] array = processesByName;
			foreach (Process process in array)
			{
				process.Dispose();
			}
			return processesByName.Length > 0;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private void Install()
	{
		Manifest manifest = pending;
		if (manifest == null || installing)
		{
			return;
		}
		installing = true;
		try
		{
			InstallNow(manifest);
		}
		finally
		{
			installing = false;
		}
	}

	private void InstallNow(Manifest m)
	{
		bool flag = false;
		try
		{
			if (busyNow || setupBusy)
			{
				SetUpdateState(string.Format(L("v{0} is ready: it installs when the current task ends.", "A v{0} esta pronta: instala-se quando a tarefa atual acabar.", "v{0} est prête : elle s'installe à la fin de la tâche en cours."), m.Version));
				return;
			}
			if (m.Files.Any((UpdateFile f) => f.Name == "victory.exe" || f.Name.StartsWith("D3D12/")) && VictoryRunning())
			{
				SetUpdateState(string.Format(L("v{0} is ready: it installs when VIctory is closed.", "A v{0} esta pronta: instala-se quando o VIctory fechar.", "v{0} est prête : elle s'installe quand VIctory est fermé."), m.Version));
				return;
			}
			foreach (UpdateFile file in m.Files)
			{
				string text = Path.Combine(pendingDir, file.Name.Replace('/', '\\'));
				if (!File.Exists(text) || new FileInfo(text).Length != file.Size || Sha256(text) != file.Sha256)
				{
					SetUpdateState(string.Format(L("v{0} was refused: {1} does not match its SHA-256.", "A v{0} foi recusada: o {1} nao bate com o SHA-256."), m.Version, file.Name));
					pending = null;
					return;
				}
			}
			foreach (UpdateFile file2 in m.Files)
			{
				string sourceFileName = Path.Combine(pendingDir, file2.Name.Replace('/', '\\'));
				if (file2.Name == "Julia's Launcher.exe")
				{
					flag = true;
					continue;
				}
				string text2 = Path.Combine(AppDir, file2.Name.Replace('/', '\\'));
				Directory.CreateDirectory(Path.GetDirectoryName(text2));
				File.Copy(sourceFileName, text2, overwrite: true);
			}
			if (flag)
			{
				string executablePath = Application.ExecutablePath;
				string text3 = Path.Combine(root, "Julia's Launcher.old.exe");
				if (File.Exists(text3))
				{
					File.Delete(text3);
				}
				File.Move(executablePath, text3);
				try
				{
					File.Copy(Path.Combine(pendingDir, "Julia's Launcher.exe"), executablePath, overwrite: true);
				}
				catch (Exception)
				{
					try
					{
						if (File.Exists(executablePath))
						{
							File.Delete(executablePath);
						}
					}
					catch (Exception)
					{
					}
					try
					{
						if (!File.Exists(executablePath))
						{
							File.Move(text3, executablePath);
						}
					}
					catch (Exception)
					{
					}
					throw;
				}
			}
			Log(string.Format(L("Updated to v{0}.", "Atualizado para a v{0}."), m.Version));
			if (m.Files.Any((UpdateFile f) => f.Name == "victory.exe"))
			{
				WriteSetting("shaders", "0");
			}
			pending = null;
			if (flag)
			{
				MessageBox.Show(this, string.Format(L("Julia's Launcher was updated to v{0}. It opens again now.", "O Julia's Launcher foi atualizado para a v{0}. Volta a abrir agora."), m.Version), "Julia's Launcher");
				using (Process process = Process.GetCurrentProcess())
				{
					using (Process.Start(Application.ExecutablePath, "--wait-pid " + process.Id))
					{
					}
				}
				Application.Exit();
			}
			else
			{
				installedVersion = m.Version;
				SetUpdateState(string.Format(L("Updated to v{0}.", "Atualizado para a v{0}."), m.Version));
			}
		}
		catch (Exception ex4)
		{
			SetUpdateState(L("Installing the update failed: ", "A instalacao do update falhou: ") + ex4.Message);
			try
			{
				File.AppendAllText(Program.ErrorFile, string.Concat(DateTime.Now, "  update: ", ex4, Environment.NewLine));
			}
			catch (Exception)
			{
			}
		}
	}

	private static bool NewestSignedRelease(out string manifestUrl, out string signatureUrl)
	{
		manifestUrl = (signatureUrl = null);
		try
		{
			string input;
			using (WebClient webClient = new WebClient())
			{
				webClient.Headers[HttpRequestHeader.UserAgent] = "Julias-Launcher";
				webClient.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
				input = Encoding.UTF8.GetString(webClient.DownloadData("https://api.github.com/repos/felicienradio/cold-project-vi/releases?per_page=15"));
			}
			JavaScriptSerializer javaScriptSerializer = new JavaScriptSerializer();
			javaScriptSerializer.MaxJsonLength = int.MaxValue;
			if (!(javaScriptSerializer.DeserializeObject(input) is object[] array))
			{
				return false;
			}
			object[] array2 = array;
			foreach (object obj in array2)
			{
				if (!(obj is Dictionary<string, object> dictionary) || (dictionary.ContainsKey("draft") && dictionary["draft"] is bool && (bool)dictionary["draft"]))
				{
					continue;
				}
				object[] array3 = (dictionary.ContainsKey("assets") ? (dictionary["assets"] as object[]) : null);
				if (array3 == null)
				{
					continue;
				}
				string text = null;
				string text2 = null;
				object[] array4 = array3;
				foreach (object obj2 in array4)
				{
					if (!(obj2 is Dictionary<string, object> dictionary2))
					{
						continue;
					}
					string text3 = dictionary2["name"] as string;
					if (dictionary2["browser_download_url"] is string text4 && text4.StartsWith("https://github.com/felicienradio/cold-project-vi/releases/download/"))
					{
						if (text3 == "manifest.json")
						{
							text = text4;
						}
						if (text3 == "manifest.sig")
						{
							text2 = text4;
						}
					}
				}
				if (text != null && text2 != null)
				{
					manifestUrl = text;
					signatureUrl = text2;
					return true;
				}
			}
		}
		catch (Exception)
		{
		}
		return false;
	}

	private static bool SignedByEdupode(byte[] data, byte[] signature)
	{
		string[] all = ReleaseKey.All;
		foreach (string xmlString in all)
		{
			try
			{
				using RSACryptoServiceProvider rSACryptoServiceProvider = new RSACryptoServiceProvider();
				rSACryptoServiceProvider.FromXmlString(xmlString);
				if (rSACryptoServiceProvider.VerifyData(data, "SHA256", signature))
				{
					return true;
				}
			}
			catch (Exception)
			{
			}
		}
		return false;
	}

	private static string Sha256(string file)
	{
		using SHA256 sHA = SHA256.Create();
		using FileStream inputStream = File.OpenRead(file);
		return string.Concat((from b in sHA.ComputeHash(inputStream)
			select b.ToString("x2")).ToArray());
	}

	private static Manifest ParseManifest(string json)
	{
		try
		{
			if (!(new JavaScriptSerializer().DeserializeObject(json) is Dictionary<string, object> dictionary))
			{
				return null;
			}
			Manifest manifest = new Manifest();
			manifest.Version = (dictionary["version"] as string) ?? "";
			Manifest manifest2 = manifest;
			if (dictionary.TryGetValue("notes", out var value))
			{
				manifest2.Notes = (value as string) ?? "";
			}
			if (dictionary.TryGetValue("notes_pt", out value))
			{
				manifest2.NotesPt = (value as string) ?? "";
			}
			object[] array = (object[])dictionary["files"];
			foreach (object obj in array)
			{
				Dictionary<string, object> dictionary2 = obj as Dictionary<string, object>;
				UpdateFile updateFile = new UpdateFile();
				updateFile.Name = dictionary2["name"] as string;
				updateFile.Url = dictionary2["url"] as string;
				updateFile.Sha256 = ((dictionary2["sha256"] as string) ?? "").ToLowerInvariant();
				updateFile.Size = Convert.ToInt64(dictionary2["size"]);
				UpdateFile updateFile2 = updateFile;
				if (!Updater.Allowed.Contains(updateFile2.Name) || updateFile2.Url == null || !updateFile2.Url.StartsWith("https://github.com/felicienradio/cold-project-vi/releases/download/") || updateFile2.Sha256.Length != 64)
				{
					return null;
				}
				manifest2.Files.Add(updateFile2);
			}
			return (manifest2.Version.Length > 0) ? manifest2 : null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private void SetUpdateState(string text)
	{
		updateState = text;
		try
		{
			BeginInvoke(new Action(RefreshUpdates));
		}
		catch (Exception)
		{
		}
	}

	private Panel UpdatesPage()
	{
		Panel panel = Page();
		int num = PageTop(panel, L("Updates", "Atualizacoes", "Mises à jour"), L("Updates", "Atualizacoes", "Mises à jour"), L("stay current.", "sempre em dia.", "toujours à jour."), null);
		updVersion = new Label
		{
			Location = new Point(40, num),
			Size = new Size(900, 28),
			Font = Theme.Mono(12f),
			ForeColor = Theme.Text,
			BackColor = Theme.Back
		};
		panel.Controls.Add(updVersion);
		num += 32;
		updState = new Label
		{
			Location = new Point(40, num),
			Size = new Size(900, 24),
			ForeColor = Theme.Cyan,
			BackColor = Theme.Back
		};
		panel.Controls.Add(updState);
		num += 26;
		updLast = new Label
		{
			Location = new Point(40, num),
			Size = new Size(900, 22),
			ForeColor = Theme.Dim,
			BackColor = Theme.Back,
			Font = Theme.Mono(8.5f)
		};
		panel.Controls.Add(updLast);
		num += 32;
		Pill pill = new Pill();
		pill.Main = true;
		pill.Text = L("Check now", "Verificar agora");
		pill.Location = new Point(40, num);
		pill.Size = new Size(160, 38);
		Pill pill2 = pill;
		pill2.Click += (object s, EventArgs e) =>
		{
			Thread thread = new Thread(() =>
			{
				CheckForUpdate(loud: true);
			});
			thread.IsBackground = true;
			thread.Start();
		};
		panel.Controls.Add(pill2);
		installNow = new Pill
		{
			Text = L("Install now", "Instalar agora"),
			Location = new Point(210, num),
			Size = new Size(160, 38),
			Enabled = false
		};
		installNow.Click += (object s, EventArgs e) =>
		{
			if (State() != GameState.None)
			{
				MessageBox.Show(this, L("Close the game first.", "Fecha o jogo primeiro."));
			}
			else
			{
				Install();
			}
		};
		panel.Controls.Add(installNow);
		num += 52;
		autoUpdateBox = new OptionCard
		{
			Text = L("Install updates on their own", "Instalar updates sozinho"),
			Line = L("While the game is not running; checked every 5 minutes", "Quando o jogo nao esta a correr; verificado de 5 em 5 minutos"),
			Location = new Point(40, num),
			Size = new Size(900, 58),
			Checked = (ReadSetting("autoupdate") != "0")
		};
		autoUpdateBox.CheckedChanged += (object s, EventArgs e) =>
		{
			WriteSetting("autoupdate", autoUpdateBox.Checked ? "1" : "0");
			if (autoUpdateBox.Checked)
			{
				TryInstall(now: false);
			}
		};
		panel.Controls.Add(autoUpdateBox);
		num += 70;
		panel.Controls.Add(new Heading(L("What changed", "O que mudou"))
		{
			Location = new Point(40, num),
			Width = 900
		});
		num += 30;
		updNotes = new TextBox
		{
			Multiline = true,
			ReadOnly = true,
			BorderStyle = BorderStyle.None,
			ForeColor = Theme.Text,
			Font = Theme.Ui(9.5f),
			ScrollBars = ScrollBars.Vertical
		};
		Kit.Dark(updNotes, "DarkMode_Explorer");
		panel.Controls.Add(FieldFor(updNotes, 40, num, 900, 220, flat: true));
		num += 232;
		panel.Controls.Add(new Label
		{
			Location = new Point(40, num),
			Size = new Size(900, 80),
			ForeColor = Theme.Dim,
			BackColor = Theme.Back,
			Font = Theme.Ui(9f),
			Text = L("Updates come only from github.com/felicienradio/cold-project-vi releases. Each one is signed with the project's release key; a file is installed only when the signature and its SHA-256 match. Anyone can check a release's files against the source code (README: 'Checking a release').", "Os updates so vem das versoes de github.com/felicienradio/cold-project-vi. Cada um e assinado com a chave de releases do projeto; um ficheiro so e instalado se a assinatura e o SHA-256 baterem certo. Qualquer pessoa pode comparar os ficheiros de uma versao com o codigo (README: 'Verificar uma versao').")
		});
		return panel;
	}

	private void RefreshUpdates()
	{
		if (updVersion != null)
		{
			updVersion.Text = "v0.3.16";
			updState.Text = updateState;
			updLast.Text = ((lastCheck == DateTime.MinValue) ? "" : (L("Last check: ", "Ultima verificacao: ") + lastCheck.ToString("HH:mm:ss") + L("  ·  next in 5 minutes", "  ·  proxima daqui a 5 minutos")));
			installNow.Enabled = pending != null;
			installNow.Invalidate();
			updNotes.Text = ((pending == null) ? "" : ((Pt && pending.NotesPt.Length > 0) ? pending.NotesPt : pending.Notes).Replace("\n", "\r\n"));
			updateBadge.Text = ((pending != null) ? ("● " + string.Format(L("Update v{0} ready", "Update v{0} pronto"), pending.Version)) : "");
			Control control = updateBadge.Parent;
			if (control != null)
			{
				control.Width = control.Width++;
			}
		}
	}

	private void ReportErrors()
	{
		Form f = new Form
		{
			Text = L("Send errors", "Enviar erros"),
			ClientSize = new Size(640, 560),
			BackColor = Theme.Back,
			ForeColor = Theme.Text,
			Font = Theme.Ui(9.75f),
			StartPosition = FormStartPosition.CenterParent,
			FormBorderStyle = FormBorderStyle.FixedDialog,
			MaximizeBox = false,
			MinimizeBox = false,
			Icon = Icon
		};
		int num = 16;
		f.Controls.Add(new Heading(L("What kind of problem?", "Que tipo de problema?"))
		{
			Location = new Point(20, num),
			Width = 600
		});
		num += 32;
		OptionCard launcherKind = new OptionCard
		{
			Text = L("Launcher errors", "Erros do launcher"),
			Line = L("The launcher closes, a button does nothing, Setup or an update fails", "O launcher fecha, um botao nao faz nada, o Preparar ou um update falha"),
			Location = new Point(20, num),
			Size = new Size(600, 58)
		};
		num += 64;
		OptionCard gameKind = new OptionCard
		{
			Text = L("Game errors", "Erros do jogo"),
			Line = L("The game closes or freezes, black squares, shaders, low FPS", "O jogo fecha ou congela, quadrados pretos, shaders, FPS baixos"),
			Location = new Point(20, num),
			Size = new Size(600, 58),
			Checked = true
		};
		launcherKind.CheckedChanged += (object s, EventArgs e) =>
		{
			if (launcherKind.Checked)
			{
				gameKind.Checked = false;
			}
			else if (!gameKind.Checked)
			{
				launcherKind.Checked = true;
			}
		};
		gameKind.CheckedChanged += (object s, EventArgs e) =>
		{
			if (gameKind.Checked)
			{
				launcherKind.Checked = false;
			}
			else if (!launcherKind.Checked)
			{
				gameKind.Checked = true;
			}
		};
		f.Controls.Add(launcherKind);
		f.Controls.Add(gameKind);
		num += 70;
		f.Controls.Add(new Heading(L("What happened? (tick any)", "O que aconteceu? (marca o que for)"))
		{
			Location = new Point(20, num),
			Width = 600
		});
		num += 30;
		string[] array = new string[10]
		{
			L("My shader is not rendering properly", "Os shaders nao aparecem bem"),
			L("Virtual memory not turning on", "A memoria virtual nao liga"),
			L("Resizable BAR shows off but it is on", "O Resizable BAR aparece desligado mas esta ligado"),
			L("The game closes or crashes", "O jogo fecha ou crasha"),
			L("PLAY does nothing / stays grey", "O JOGAR nao faz nada / fica cinzento"),
			L("Preparing shaders fails", "Preparar os shaders falha"),
			L("Low FPS or stutter", "FPS baixos ou engasgos"),
			L("Black squares or wrong colours", "Quadrados pretos ou cores erradas"),
			L("Frame generation test (how it ran)", "Teste da geracao de frames (como correu)"),
			L("AMD card: tried the edupode engine", "Placa AMD: experimentei o motor edupode")
		};
		List<Toggle> boxes = new List<Toggle>();
		for (int num2 = 0; num2 < array.Length; num2++)
		{
			Toggle toggle = new Toggle();
			toggle.Text = array[num2];
			toggle.Location = new Point(20 + num2 % 2 * 300, num + num2 / 2 * 30);
			toggle.Width = 296;
			Toggle toggle2 = toggle;
			boxes.Add(toggle2);
			f.Controls.Add(toggle2);
		}
		num += (array.Length + 1) / 2 * 30 + 8;
		f.Controls.Add(Small(L("In your words (what you did, what you saw):", "Por palavras tuas (o que fizeste, o que viste):"), 20, num, 600));
		num += 22;
		TextBox words = new TextBox
		{
			Location = new Point(20, num),
			Size = new Size(600, 70),
			Multiline = true,
			BackColor = Theme.Card,
			ForeColor = Theme.Text,
			BorderStyle = BorderStyle.FixedSingle,
			Font = Theme.Ui(9.5f)
		};
		f.Controls.Add(words);
		num += 82;
		Pill make = new Pill
		{
			Main = true,
			Text = L("Make the package", "Criar o pacote"),
			Location = new Point(420, num),
			Size = new Size(200, 40)
		};
		Pill pill = new Pill();
		pill.Text = L("Cancel", "Cancelar");
		pill.Location = new Point(290, num + 3);
		pill.Size = new Size(120, 34);
		Pill pill2 = pill;
		pill2.Click += (object s, EventArgs e) =>
		{
			f.Close();
		};
		make.Click += (object s, EventArgs e) =>
		{
			make.Enabled = false;
			make.Text = L("Collecting...", "A juntar...");
			bool game = gameKind.Checked;
			List<string> ticked = (from b in boxes
				where b.Checked
				select b.Text).ToList();
			string said = words.Text.Trim();
			Thread thread = new Thread(() =>
			{
				string zip = null;
				string summary = null;
				string error = null;
				try
				{
					zip = MakeReport(game, ticked, said, out summary);
				}
				catch (Exception ex)
				{
					error = ex.Message;
				}
				try
				{
					BeginInvoke((Action)(() =>
					{
						try
						{
							if (!f.IsDisposed)
							{
								f.Close();
							}
						}
						catch (Exception)
						{
						}
						if (zip == null)
						{
							MessageBox.Show(this, L("The package could not be made: ", "Nao deu para criar o pacote: ") + error, "Julia's Launcher");
						}
						else
						{
							try
							{
								Clipboard.SetText(summary);
							}
							catch (Exception)
							{
							}
							try
							{
								Process.Start("explorer.exe", "/select,\"" + zip + "\"");
							}
							catch (Exception)
							{
							}
							string body = string.Format(L("Done: {0} is on your Desktop.\r\n\r\n•  Send: one click, no account needed. The logs (the end of each one, settings; your user name taken out) go to the developers' private report box, and a public bug report opens on GitHub (felicienradio/cold-project-vi) with the summary only: your PC's parts, versions, preset, settings and what you ticked and wrote. You can follow it there.\r\n•  Send privately: the same, without the public GitHub report.", "Feito: {0} esta no Ambiente de trabalho.\r\n\r\n•  Enviar: um clique, sem conta. Os logs (o fim de cada um, definicoes; sem o teu nome de utilizador) vao para a caixa privada de relatorios dos programadores, e abre-se um relatorio publico no GitHub (felicienradio/cold-project-vi) so com o resumo: as pecas do PC, versoes, predefinicao, definicoes e o que marcaste e escreveste. Podes acompanha-lo la.\r\n•  Enviar em privado: o mesmo, sem o relatorio publico no GitHub.", "Terminé : {0} est sur ton Bureau.\r\n\r\n•  Envoyer : un clic, sans compte. Les logs (la fin de chacun, les réglages ; sans ton nom d'utilisateur) partent dans la boîte à rapports privée des développeurs, et un rapport de bug public s'ouvre sur GitHub (felicienradio/cold-project-vi) avec seulement le résumé : les composants de ton PC, les versions, le préréglage, les réglages et ce que tu as coché et écrit. Tu pourras le suivre là-bas.\r\n•  Envoyer en privé : la même chose, sans le rapport public sur GitHub."), Path.GetFileName(zip));
							int num3 = Notice(L("Send the report", "Enviar o relatorio"), body, L("Send", "Enviar", "Envoyer"), L("Send privately", "Enviar em privado", "Envoyer en privé"));
							if (num3 == 1)
							{
								SendReport(zip, game, open: true);
							}
							if (num3 == 2)
							{
								SendReport(zip, game, open: false);
							}
						}
					}));
				}
				catch (Exception)
				{
				}
			});
			thread.IsBackground = true;
			thread.Start();
		};
		f.Controls.Add(make);
		f.Controls.Add(pill2);
		f.ClientSize = new Size(640, num + 60);
		f.ShowDialog(this);
	}

	private string MakeReport(bool game, List<string> ticked, string said, out string summary)
	{
		HardwareInfo hardwareInfo = hw ?? ReadHardware();
		string text = DateTime.Now.ToString("yyyyMMdd-HHmm");
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
		string path = "VI-" + (game ? "game" : "launcher") + "-errors-" + hardwareInfo.Id.Replace("VI-", "") + "-" + text;
		string text2 = Path.Combine(folderPath, path);
		Directory.CreateDirectory(text2);
		string activity = "";
		string options = "";
		Invoke((Action)(() =>
		{
			activity = logBox.Text;
			options = string.Join("\r\n", (from kv in Environment2()
				where kv.Key != "VICTORY_GAME_DIR"
				select kv.Key + "=" + kv.Value).ToArray());
		}));
		List<string> list = Missing();
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Julia's Launcher error report");
		stringBuilder.AppendLine("ID: " + hardwareInfo.Id);
		stringBuilder.AppendLine("Kind: " + (game ? "game errors" : "launcher errors"));
		stringBuilder.AppendLine("Launcher: v0.3.16   Engine: " + EngineName + "   Preset: " + Selected.Name);
		stringBuilder.AppendLine("GPU: " + hardwareInfo.Gpu + " (" + hardwareInfo.VramMb / 1024 + " GB, driver " + hardwareInfo.Driver + ")   " + RebarText() + "   VIctory runs on: " + probeAdapter);
		stringBuilder.AppendLine("GPU type: " + (integratedOnly ? "integrated only" : "dedicated") + "   Adapters: " + string.Join("; ", adapters.ConvertAll((GpuAdapter a) => a.VendorName + " " + a.Name + " (" + a.DedicatedMb + " MB own, " + a.SharedMb + " MB shared" + (a.Integrated ? ", integrated" : "") + ")").ToArray()));
		stringBuilder.AppendLine("CPU: " + hardwareInfo.Cpu + "   RAM: " + hardwareInfo.RamGb + " GB   Board: " + hardwareInfo.BoardMaker + " " + hardwareInfo.Board);
		stringBuilder.AppendLine("Windows: " + hardwareInfo.Os + "   Disk: " + hardwareInfo.Disk + (hardwareInfo.Laptop ? "   laptop" : ""));
		stringBuilder.AppendLine("Missing: " + ((list.Count == 0) ? "nothing" : string.Join(", ", list.ToArray())));
		stringBuilder.AppendLine("Shaders cached: " + shaderFiles + ((exeProblem != null) ? ("   Game file: " + exeProblem) : ""));
		if (ticked.Count > 0)
		{
			stringBuilder.AppendLine("Ticked: " + string.Join("; ", ticked.ToArray()));
		}
		if (said.Length > 0)
		{
			stringBuilder.AppendLine("In their words: " + said);
		}
		summary = Scrub(stringBuilder.ToString());
		File.WriteAllText(Path.Combine(text2, "summary.txt"), summary + "\r\nOptions:\r\n" + Scrub(options));
		File.WriteAllText(Path.Combine(text2, "launcher-activity.txt"), Scrub(activity));
		CopyScrubbed(settingsFile, Path.Combine(text2, "launcher.ini"));
		CopyScrubbed(Program.ErrorFile, Path.Combine(text2, "launcher-errors.txt"));
		string path2 = Path.Combine(dataRoot, "logs");
		if (Directory.Exists(path2))
		{
			List<string> list2 = (from p in Directory.GetFiles(path2, "*.log")
				where !Path.GetFileName(p).StartsWith("session-")
				select p).ToList();
			if (game)
			{
				list2.AddRange(Directory.GetFiles(path2, "session-*.log").OrderByDescending(File.GetLastWriteTime).Take(3));
			}
			foreach (string item in list2)
			{
				CopyScrubbed(item, Path.Combine(text2, Path.GetFileName(item)));
			}
		}
		if (game)
		{
			StringBuilder stringBuilder2 = new StringBuilder();
			try
			{
				string folder = null;
				Invoke((Action)(() =>
				{
					folder = gameBox.Text.Trim();
				}));
				if (Directory.Exists(folder))
				{
					foreach (string item2 in from p in Directory.GetFiles(folder)
						where p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
						select p)
					{
						stringBuilder2.AppendLine($"{new FileInfo(item2).Length,14:N0}  {Path.GetFileName(item2)}");
					}
				}
			}
			catch (Exception)
			{
			}
			File.WriteAllText(Path.Combine(text2, "game-folder.txt"), stringBuilder2.ToString());
			if (OwnVictoryExe != null)
			{
				File.WriteAllText(Path.Combine(text2, "victory-gpu.txt"), Scrub(RunCapture(OwnVictoryExe, "gpu")));
			}
		}
		string text3 = text2 + ".zip";
		if (File.Exists(text3))
		{
			File.Delete(text3);
		}
		ZipFile.CreateFromDirectory(text2, text3, CompressionLevel.Optimal, includeBaseDirectory: false);
		return text3;
	}

	private void SendReport(string zip, bool game, bool open)
	{
		string dir = zip.Substring(0, zip.Length - 4);
		HardwareInfo hwNow = hw;
		Thread thread = new Thread(() =>
		{
			string issue = null;
			string viId = "";
			bool sent = false;
			string said;
			try
			{
				viId = (hwNow ?? ReadHardware()).Id;
				byte[] array = null;
				int[] array2 = new int[3] { 409600, 153600, 40960 };
				foreach (int tail in array2)
				{
					array = CompactZip(dir, tail);
					if (array.Length <= 1843200)
					{
						break;
					}
				}
				if (array.Length > 1843200)
				{
					throw new Exception(L("the package is too big to send; use the GitHub issue", "o pacote e grande demais; usa a issue do GitHub"));
				}
				ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
				HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create("https://vi-hardware.edupodept.workers.dev/v1/report");
				httpWebRequest.Method = "POST";
				httpWebRequest.ContentType = "application/zip";
				httpWebRequest.UserAgent = "Julias-Launcher";
				httpWebRequest.Timeout = 60000;
				httpWebRequest.Headers["X-VI-Id"] = viId;
				httpWebRequest.Headers["X-VI-Kind"] = (game ? "game" : "launcher");
				if (open)
				{
					httpWebRequest.Headers["X-VI-Public"] = "1";
				}
				httpWebRequest.ContentLength = array.Length;
				using (Stream stream = httpWebRequest.GetRequestStream())
				{
					stream.Write(array, 0, array.Length);
				}
				string input;
				using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
				{
					using StreamReader streamReader = new StreamReader(httpWebResponse.GetResponseStream());
					input = streamReader.ReadToEnd();
				}
				string value = Regex.Match(input, "\"n\"\\s*:\\s*(\\d+)").Groups[1].Value;
				Match match = Regex.Match(input, "\"issue\"\\s*:\\s*\"(https://github\\.com/[^\"]+)\"");
				issue = (match.Success ? match.Groups[1].Value : null);
				said = ((issue != null) ? string.Format(L("Report #{0} sent. Its public bug report is on GitHub: {1}\r\n\r\nYour hardware ID is {2} (copied).", "Relatorio n.o {0} enviado. O relatorio publico esta no GitHub: {1}\r\n\r\nO teu ID de hardware e {2} (copiado).", "Rapport n° {0} envoyé. Son rapport public est sur GitHub : {1}\r\n\r\nTon ID matériel est {2} (copié)."), value, issue, viId) : string.Format(L("Sent to edupode: report #{0}.\r\n\r\nYour hardware ID is {1} (copied). Paste it on Discord, in \"{2}\", so edupode can find your reports.", "Enviado ao edupode: relatorio n.o {0}.\r\n\r\nO teu ID de hardware e {1} (copiado). Cola-o no Discord, em \"{2}\", para o edupode encontrar os teus relatorios."), value, viId, "Send bugs here please"));
				sent = true;
			}
			catch (WebException ex)
			{
				HttpWebResponse httpWebResponse2 = ex.Response as HttpWebResponse;
				said = ((httpWebResponse2 != null && httpWebResponse2.StatusCode == (HttpStatusCode)429) ? L("Too many reports this hour: try again later, or use the GitHub issue.", "Demasiados relatorios nesta hora: tenta mais tarde, ou usa a issue do GitHub.") : (L("Could not send the report: ", "Nao deu para enviar o relatorio: ") + ex.Message));
				httpWebResponse2?.Close();
			}
			catch (Exception ex2)
			{
				said = L("Could not send the report: ", "Nao deu para enviar o relatorio: ") + ex2.Message;
			}
			SafeLog(said);
			try
			{
				BeginInvoke((Action)(() =>
				{
					if (sent)
					{
						try
						{
							Clipboard.SetText(viId);
						}
						catch (Exception)
						{
						}
					}
					if (sent && issue != null)
					{
						if (MessageBox.Show(this, said + L("\r\n\r\nOpen it now?", "\r\n\r\nAbrir agora?", "\r\n\r\nL'ouvrir maintenant ?"), "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) == DialogResult.Yes)
						{
							OpenUrl(issue);
						}
					}
					else if (sent && MessageBox.Show(this, said + L("\r\n\r\nOpen Discord now?", "\r\n\r\nAbrir o Discord agora?"), "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) == DialogResult.Yes)
					{
						OpenUrl("https://discord.gg/648WEXCAsZ");
					}
					else if (!sent)
					{
						MessageBox.Show(this, said, "Julia's Launcher");
					}
				}));
			}
			catch (Exception)
			{
			}
		});
		thread.IsBackground = true;
		thread.Start();
	}

	private static byte[] CompactZip(string dir, int tail)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using (ZipArchive zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
		{
			string[] files = Directory.GetFiles(dir);
			foreach (string text in files)
			{
				byte[] array = File.ReadAllBytes(text);
				if (text.EndsWith(".log", StringComparison.OrdinalIgnoreCase) && array.Length > tail)
				{
					array = array.Skip(array.Length - tail).ToArray();
				}
				ZipArchiveEntry zipArchiveEntry = zipArchive.CreateEntry(Path.GetFileName(text), CompressionLevel.Optimal);
				using Stream stream = zipArchiveEntry.Open();
				stream.Write(array, 0, array.Length);
			}
		}
		return memoryStream.ToArray();
	}

	private static string IssueUrl(bool game, List<string> ticked, string said, string summary, string zipName)
	{
		string text;
		if (ticked.Count > 0)
		{
			text = ticked[0];
		}
		else if (said.Length > 0)
		{
			text = said;
		}
		else
		{
			text = (game ? "Game problem" : "Launcher problem");
		}
		if (text.Length > 70)
		{
			text = text.Substring(0, 70) + "...";
		}
		string stringToEscape = "[" + (game ? "game" : "launcher") + "] " + text;
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("<!-- Drag " + zipName + " from your Desktop into this box before submitting. -->");
		stringBuilder.AppendLine("**Package:** drag `" + zipName + "` (on your Desktop) here.");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("```text");
		stringBuilder.AppendLine((summary.Length > 4500) ? (summary.Substring(0, 4500) + "\n...") : summary.TrimEnd());
		stringBuilder.AppendLine("```");
		return "https://github.com/edupode/cold-project-vi/issues/new?labels=" + Uri.EscapeDataString(game ? "bug,game" : "bug,launcher") + "&title=" + Uri.EscapeDataString(stringToEscape) + "&body=" + Uri.EscapeDataString(stringBuilder.ToString());
	}

	private static string Scrub(string text)
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string input = (text ?? "").Replace(folderPath, "%USERPROFILE%");
		return Regex.Replace(input, "(?i)([\\\\/]Users[\\\\/])[^\\\\/\\r\\n\"']+", "$1<user>");
	}

	private static void CopyScrubbed(string from, string to)
	{
		try
		{
			if (!File.Exists(from))
			{
				return;
			}
			using FileStream fileStream = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			if (fileStream.Length > 20971520)
			{
				fileStream.Seek(-20971520L, SeekOrigin.End);
			}
			File.WriteAllText(to, Scrub(new StreamReader(fileStream).ReadToEnd()));
		}
		catch (Exception)
		{
		}
	}

	private string CustomTag()
	{
		double.TryParse(custom.Scale, NumberStyles.Float, CultureInfo.InvariantCulture, out var result);
		if (!(result < 0.999))
		{
			return "1080p";
		}
		return (int)Math.Round(1080.0 * result) + "p";
	}

	private void LoadCustom()
	{
		CustomProfile customProfile = CustomProfile.FromText(ReadSetting("custom"), out var _);
		if (customProfile != null)
		{
			custom = customProfile;
		}
	}

	private void SaveCustom()
	{
		WriteSetting("custom", custom.Text());
	}

	private void ApplyCustom(Dictionary<string, string> e)
	{
		CustomProfile customProfile = custom;
		if (customProfile.Scale == "1.0")
		{
			e.Remove("VICTORY_RENDER_SCALE");
			e.Remove("VICTORY_OWN_UPSCALER");
		}
		else
		{
			e["VICTORY_RENDER_SCALE"] = customProfile.Scale;
			e["VICTORY_OWN_UPSCALER"] = "1";
		}
		if (customProfile.TextureMax == 0)
		{
			e.Remove("VICTORY_TEXTURE_MAX");
		}
		else
		{
			e["VICTORY_TEXTURE_MAX"] = customProfile.TextureMax.ToString();
		}
		e["VICTORY_CARD_BUFFERS_MB"] = customProfile.CardBuffersMb.ToString();
		e["VICTORY_RIPE_BLOCKS"] = customProfile.RipeBlocks.ToString();
		e["VICTORY_TEXTURE_STAGE_MB"] = customProfile.StageMb.ToString();
		if (customProfile.Fps == 60)
		{
			e["VICTORY_FPS_MAX"] = "60";
		}
		else
		{
			e.Remove("VICTORY_FPS_MAX");
		}
		if (customProfile.Guards)
		{
			e.Remove("VICTORY_PRESSURE_FREE_MB");
			e.Remove("VICTORY_PRESSURE_CLEAR_MB");
			e.Remove("VICTORY_TEXTURE_RAM_GUARD");
		}
		e["VICTORY_LEVELS_ON_DEMAND"] = (customProfile.LevelsOnDemand ? "1" : "0");
		if (customProfile.CardBuffersMb <= 768)
		{
			e.Remove("VICTORY_WINDOWS_BAR_MB");
		}
	}

	private Panel CustomPage()
	{
		LoadCustom();
		Panel panel = Page();
		int num = PageTop(panel, L("Advanced Tweaks", "Ajustes avancados", "Réglages avancés"), L("Tweaks", "Ajustes", "Réglages"), L("of your own.", "so teus.", "à toi."), null);
		Label label = Kit.Text(L("Your own values. They count when the Custom card is picked on the Play page. Lower values use less memory and give a softer picture.", "Os teus proprios valores. Contam quando o cartao Personalizado esta escolhido na pagina Jogar. Valores mais baixos usam menos memoria e dao uma imagem mais suave.", "Tes propres valeurs. Elles comptent quand la carte Personnalisé est choisie sur la page Jouer. Des valeurs plus basses utilisent moins de mémoire et donnent une image plus douce."), Theme.Ui(9.5f), Theme.Soft, Theme.Back, 40, num, 900);
		panel.Controls.Add(label);
		num += label.Height + 14;
		RoundCard values = new RoundCard
		{
			Location = new Point(40, num),
			Width = 900
		};
		int vy = 10;
		Func<string, string, object[], ComboBox> func = (string text5, string note, object[] items) =>
		{
			Font f = Theme.UiBold(9.5f);
			Font f2 = Theme.Ui(9f);
			int num7 = Kit.TextH(text5, f, 250);
			int num8 = Kit.TextH(note, f2, 346);
			int num9 = Math.Max(48, Math.Max(num7, num8) + 16);
			Label value = Kit.Text(text5, f, Theme.Text, Theme.Card, 20, vy + (num9 - num7) / 2, 250);
			Dropdown dropdown = new Dropdown
			{
				Location = new Point(284, 0),
				Width = 230
			};
			dropdown.Top = vy + (num9 - dropdown.Face) / 2;
			dropdown.Items.AddRange(items);
			dropdown.SelectedIndexChanged += (object s, EventArgs e) =>
			{
				ReadCustomFromPage();
			};
			Label value2 = Kit.Text(note, f2, Theme.Dim, Theme.Card, 534, vy + (num9 - num8) / 2, 346);
			values.Controls.Add(value);
			values.Controls.Add(dropdown);
			values.Controls.Add(value2);
			vy += num9;
			return dropdown;
		};
		cScale = func(L("World resolution", "Resolucao do mundo", "Résolution du monde"), L("rebuilt to 1080p by the game's upscaler", "reconstruida para 1080p pelo upscaler do jogo", "reconstruite en 1080p par l'upscaler du jeu"), new object[5] { "1080p (1.0)", "810p (0.75)", "720p (0.667)", "540p (0.5)", "432p (0.4)" });
		cTex = func(L("Largest texture", "Maior textura", "Plus grande texture"), L("smaller: less video memory", "menor: menos VRAM", "plus petite : moins de VRAM"), new object[4]
		{
			L("no cap", "sem limite", "sans limite"),
			"2048",
			"1024",
			"512"
		});
		cBuf = func(L("Game buffers on the card", "Buffers do jogo na placa", "Tampons du jeu sur la carte"), L("more: crowds and fights hold up", "mais: multidoes e lutas aguentam melhor", "plus : foules et combats tiennent mieux"), new object[5] { "512 MB", "768 MB", "1024 MB", "1536 MB", "2048 MB" });
		cRipe = func(L("Texture blocks at once", "Blocos de texturas de cada vez", "Blocs de textures à la fois"), L("2: less memory, 4: faster", "2: menos memoria, 4: mais rapido", "2 : moins de mémoire, 4 : plus rapide"), new object[2] { "2", "4" });
		cStage = func(L("Texture batch size", "Tamanho do lote de texturas", "Taille du lot de textures"), L("smaller: less RAM", "menor: menos RAM", "plus petit : moins de RAM"), new object[3] { "64 MB", "128 MB", "384 MB" });
		cFps = func(L("FPS clock", "Relogio de FPS", "Horloge FPS"), L("60 lets the game pass 30 FPS", "60 deixa o jogo passar dos 30 FPS", "60 laisse le jeu dépasser 30 FPS"), new object[2] { "30", "60" });
		values.Height = vy + 10;
		panel.Controls.Add(values);
		num += values.Height + 14;
		int num2 = 445;
		string text = L("Memory guards", "Protecoes de memoria", "Protections mémoire");
		cGuards = new OptionCard
		{
			Text = text,
			Line = L("Hold texture prefetch back early (16 GB of RAM or less)", "Travam cedo o pre-carregamento de texturas (16 GB de RAM ou menos)", "Retiennent tôt le préchargement des textures (16 Go de RAM ou moins)"),
			Location = new Point(40, num),
			Size = new Size(num2, 72)
		};
		cLevels = new OptionCard
		{
			Text = L("Texture levels on demand", "Niveis de texturas a pedido", "Niveaux de textures à la demande"),
			Line = L("Less video memory, more pop-in", "Menos VRAM, mais texturas a aparecer tarde", "Moins de VRAM, plus d'apparitions tardives"),
			Location = new Point(40 + num2 + 10, num),
			Size = new Size(num2, 72)
		};
		cGuards.CheckedChanged += (object s, EventArgs e) =>
		{
			ReadCustomFromPage();
		};
		cLevels.CheckedChanged += (object s, EventArgs e) =>
		{
			ReadCustomFromPage();
		};
		panel.Controls.Add(cGuards);
		panel.Controls.Add(cLevels);
		num += 90;
		string text2 = L("Play with these (Custom)", "Jogar com estes (Personalizado)", "Jouer avec ceux-ci (Personnalisé)");
		Pill pill = new Pill();
		pill.Main = true;
		pill.Text = text2;
		pill.Size = new Size(Kit.PillW(text2, 9.5f, 230), 40);
		Pill pill2 = pill;
		pill2.Click += (object s, EventArgs e) =>
		{
			SelectMode(6);
			SaveSettings();
			Show(0);
		};
		Pill pill3 = new Pill();
		pill3.Text = L("Back to Play", "Voltar a Jogar", "Retour à Jouer");
		pill3.Size = new Size(150, 40);
		Pill pill4 = pill3;
		pill4.Click += (object s, EventArgs e) =>
		{
			Show(0);
		};
		Func<int, string> func2 = (int k) => modes[k].Name;
		Dropdown from = new Dropdown
		{
			Width = 270
		};
		from.Items.Add(L("Start from a preset...", "Partir de uma predefinicao...", "Partir d'un préréglage..."));
		int[] array = new int[5] { 0, 1, 2, 5, 3 };
		foreach (int arg in array)
		{
			from.Items.Add(func2(arg));
		}
		from.SelectedIndex = 0;
		from.SelectedIndexChanged += (object s, EventArgs e) =>
		{
			if (from.SelectedIndex > 0)
			{
				int k = (new int[5] { 0, 1, 2, 5, 3 })[from.SelectedIndex - 1];
				custom = PresetProfile(k);
				SaveCustom();
				RefreshCustom();
				from.SelectedIndex = 0;
			}
		};
		int num4 = Kit.Row(panel, 40, num, 900 - from.Width - 10, pill2, pill4);
		from.Location = new Point(940 - from.Width, num + (40 - from.Face) / 2);
		panel.Controls.Add(from);
		num = num4 + 22;
		panel.Controls.Add(new Heading(L("Share a profile", "Partilhar um perfil", "Partager un profil"))
		{
			Location = new Point(40, num),
			Width = 900
		});
		num += 34;
		RoundCard roundCard = new RoundCard();
		roundCard.Location = new Point(40, num);
		roundCard.Width = 900;
		RoundCard roundCard2 = roundCard;
		int num5 = 18;
		string text3 = L("Copy my profile", "Copiar o meu perfil", "Copier mon profil");
		string text4 = L("Apply", "Aplicar", "Appliquer");
		int num6 = Math.Max(Kit.PillW(text3, 9.5f, 160), Kit.PillW(text4, 9.5f, 160));
		roundCard2.Controls.Add(Kit.Text(L("My profile code:", "O meu codigo de perfil:", "Mon code de profil :"), Theme.UiBold(9f), Theme.Dim, Theme.Card, 20, num5, 860));
		num5 += 24;
		codeBox = new TextBox
		{
			ReadOnly = true,
			ForeColor = Theme.Cyan,
			Font = Theme.Mono(9.5f)
		};
		Kit.Field(roundCard2, codeBox, 20, num5, 860 - num6 - 12);
		Pill pill5 = new Pill();
		pill5.Text = text3;
		pill5.Size = new Size(num6, 36);
		pill5.Location = new Point(880 - num6, num5);
		Pill pill6 = pill5;
		pill6.Click += (object s, EventArgs e) =>
		{
			Clipboard.SetText(custom.Code());
			Log(L("Profile code copied: ", "Codigo do perfil copiado: ", "Code du profil copié : ") + custom.Code());
		};
		roundCard2.Controls.Add(pill6);
		num5 += 56;
		roundCard2.Controls.Add(Kit.Text(L("Paste a profile code here:", "Cola aqui um codigo de perfil:", "Colle un code de profil ici :"), Theme.UiBold(9f), Theme.Dim, Theme.Card, 20, num5, 860));
		num5 += 24;
		pasteBox = new TextBox
		{
			ForeColor = Theme.Text,
			Font = Theme.Mono(9.5f)
		};
		Kit.Field(roundCard2, pasteBox, 20, num5, 860 - num6 - 12);
		Pill pill7 = new Pill();
		pill7.Main = true;
		pill7.Text = text4;
		pill7.Size = new Size(num6, 36);
		pill7.Location = new Point(880 - num6, num5);
		Pill pill8 = pill7;
		pill8.Click += (object s, EventArgs e) =>
		{
			ApplyPasted();
		};
		roundCard2.Controls.Add(pill8);
		num5 += 50;
		Label label2 = Kit.Text(L("A code only holds the values on this page; the launcher shows what changes before it applies one.", "Um codigo so tem os valores desta pagina; o launcher mostra o que muda antes de o aplicar.", "Un code ne contient que les valeurs de cette page ; le launcher montre ce qui change avant d'en appliquer un."), Theme.Ui(9f), Theme.Dim, Theme.Card, 20, num5, 860);
		roundCard2.Controls.Add(label2);
		roundCard2.Height = num5 + label2.Height + 16;
		panel.Controls.Add(roundCard2);
		panel.Controls.Add(new Label
		{
			Location = new Point(40, num + roundCard2.Height + 10),
			Size = new Size(10, 14),
			BackColor = Theme.Back
		});
		return panel;
	}

	private CustomProfile PresetProfile(int k)
	{
		CustomProfile customProfile = new CustomProfile();
		Mode mode = modes[k];
		customProfile.Scale = mode.Scale ?? "1.0";
		customProfile.Fps = (mode.UltraLow ? 30 : 60);
		if (mode.UltraLow)
		{
			customProfile.TextureMax = 512;
			customProfile.CardBuffersMb = 512;
			customProfile.RipeBlocks = 2;
			customProfile.StageMb = 64;
			customProfile.Guards = true;
			customProfile.LevelsOnDemand = true;
		}
		else if (mode.Modest)
		{
			customProfile.TextureMax = 1024;
			customProfile.CardBuffersMb = 768;
			customProfile.RipeBlocks = 2;
			customProfile.StageMb = 128;
			customProfile.Guards = true;
			customProfile.LevelsOnDemand = false;
		}
		else
		{
			customProfile.TextureMax = 2048;
			customProfile.CardBuffersMb = 1536;
			customProfile.RipeBlocks = 4;
			customProfile.StageMb = 384;
			customProfile.Guards = ramGb > 0.0 && ramGb < 24.0;
			customProfile.LevelsOnDemand = false;
		}
		return customProfile;
	}

	private void RefreshCustom()
	{
		if (cScale != null)
		{
			fillingCustom = true;
			cScale.SelectedIndex = Math.Max(0, Array.IndexOf(CustomProfile.Scales, custom.Scale));
			cTex.SelectedIndex = Math.Max(0, Array.IndexOf(CustomProfile.TextureMaxes, custom.TextureMax));
			cBuf.SelectedIndex = Math.Max(0, Array.IndexOf(CustomProfile.Buffers, custom.CardBuffersMb));
			cRipe.SelectedIndex = Math.Max(0, Array.IndexOf(CustomProfile.Ripes, custom.RipeBlocks));
			cStage.SelectedIndex = Math.Max(0, Array.IndexOf(CustomProfile.Stages, custom.StageMb));
			cFps.SelectedIndex = Math.Max(0, Array.IndexOf(CustomProfile.Fpses, custom.Fps));
			cGuards.Checked = custom.Guards;
			cLevels.Checked = custom.LevelsOnDemand;
			codeBox.Text = custom.Code();
			fillingCustom = false;
		}
	}

	private void ReadCustomFromPage()
	{
		if (!fillingCustom && cScale.SelectedIndex >= 0)
		{
			custom.Scale = CustomProfile.Scales[cScale.SelectedIndex];
			custom.TextureMax = CustomProfile.TextureMaxes[Math.Max(0, cTex.SelectedIndex)];
			custom.CardBuffersMb = CustomProfile.Buffers[Math.Max(0, cBuf.SelectedIndex)];
			custom.RipeBlocks = CustomProfile.Ripes[Math.Max(0, cRipe.SelectedIndex)];
			custom.StageMb = CustomProfile.Stages[Math.Max(0, cStage.SelectedIndex)];
			custom.Fps = CustomProfile.Fpses[Math.Max(0, cFps.SelectedIndex)];
			custom.Guards = cGuards.Checked;
			custom.LevelsOnDemand = cLevels.Checked;
			codeBox.Text = custom.Code();
			SaveCustom();
			Refresh2();
		}
	}

	private void ApplyPasted()
	{
		CustomProfile customProfile = CustomProfile.FromCode(pasteBox.Text, out var why);
		if (customProfile == null)
		{
			MessageBox.Show(this, L("This code cannot be used: ", "Este codigo nao serve: ") + why + ".", "Julia's Launcher", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		List<string> changes = new List<string>();
		Action<string, string, string> action = (string what, string a, string b) =>
		{
			if (a != b)
			{
				changes.Add("  •  " + what + ": " + a + "  →  " + b);
			}
		};
		action(L("World resolution", "Resolucao do mundo"), custom.Scale, customProfile.Scale);
		action(L("Largest texture", "Maior textura"), (custom.TextureMax == 0) ? "-" : custom.TextureMax.ToString(), (customProfile.TextureMax == 0) ? "-" : customProfile.TextureMax.ToString());
		action(L("Game buffers on the card", "Buffers do jogo na placa"), custom.CardBuffersMb + " MB", customProfile.CardBuffersMb + " MB");
		action(L("Texture blocks at once", "Blocos de texturas de cada vez"), custom.RipeBlocks.ToString(), customProfile.RipeBlocks.ToString());
		action(L("Texture batch size", "Tamanho do lote de texturas"), custom.StageMb + " MB", customProfile.StageMb + " MB");
		action(L("FPS clock", "Relogio de FPS"), custom.Fps.ToString(), customProfile.Fps.ToString());
		action(L("Memory guards", "Protecoes de memoria"), custom.Guards ? "on" : "off", customProfile.Guards ? "on" : "off");
		action(L("Texture levels on demand", "Niveis de texturas a pedido"), custom.LevelsOnDemand ? "on" : "off", customProfile.LevelsOnDemand ? "on" : "off");
		if (changes.Count == 0)
		{
			MessageBox.Show(this, L("This profile is the same as yours.", "Este perfil e igual ao teu."), "Julia's Launcher");
			return;
		}
		string text = L("This code changes:\r\n\r\n", "Este codigo muda:\r\n\r\n") + string.Join("\r\n", changes.ToArray()) + L("\r\n\r\nApply it and play with Custom?", "\r\n\r\nAplicar e jogar com Personalizado?");
		if (MessageBox.Show(this, text, "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
		{
			custom = customProfile;
			SaveCustom();
			RefreshCustom();
			SelectMode(6);
			SaveSettings();
			pasteBox.Text = "";
			Log(L("Profile applied: ", "Perfil aplicado: ") + custom.Code());
		}
	}

	private Panel ShadersPage()
	{
		Panel page = Page();
		int num = PageTop(page, L("Shaders", "Shaders", "Shaders"), L("Shaders", "Shaders", "Shaders"), L("kept warm.", "sempre prontos.", "au chaud."), L("The emulator, its shader cache and how healthy both are, live.", "O emulador, a cache de shaders e a saude de ambos, ao vivo.", "L'émulateur, son cache de shaders et leur état, en direct."));
		RoundCard roundCard = new RoundCard();
		roundCard.Location = new Point(40, num);
		roundCard.Width = 900;
		roundCard.Height = 76;
		roundCard.Flat = true;
		roundCard.BackColor = Theme.Back;
		RoundCard roundCard2 = roundCard;
		string text = L("Build VIctory", "Compilar VIctory", "Compiler VIctory");
		string text2 = L("Prepare shaders", "Preparar shaders", "Préparer les shaders");
		buildButton = new Pill
		{
			Text = text,
			Size = new Size(Kit.PillW(text, 9.5f, 140), 38)
		};
		prepareButton = new Pill
		{
			Main = true,
			Text = text2,
			Size = new Size(Kit.PillW(text2, 9.5f, 160), 38)
		};
		prepareButton.Location = new Point(884 - prepareButton.Width, (roundCard2.Height - 38) / 2);
		buildButton.Location = new Point(prepareButton.Left - 10 - buildButton.Width, prepareButton.Top);
		buildButton.Click += (object s, EventArgs e) =>
		{
			Build();
		};
		prepareButton.Click += (object s, EventArgs e) =>
		{
			Prepare();
		};
		buildStatus = new Label
		{
			Location = new Point(18, 10),
			Size = new Size(buildButton.Left - 18 - 16, 56),
			ForeColor = Theme.Dim,
			BackColor = Theme.Back,
			Font = Theme.Ui(9f),
			UseMnemonic = false,
			TextAlign = ContentAlignment.MiddleLeft
		};
		buildStatus.TextChanged += (object s, EventArgs e) =>
		{
			shaderTips.SetToolTip(buildStatus, buildStatus.Text);
		};
		roundCard2.Controls.Add(buildStatus);
		roundCard2.Controls.Add(buildButton);
		roundCard2.Controls.Add(prepareButton);
		page.Controls.Add(roundCard2);
		num += roundCard2.Height + 18;
		page.Controls.Add(new Heading(L("Shader and pipeline health (live)", "Saude dos shaders e pipelines (ao vivo)", "État des shaders et pipelines (en direct)"))
		{
			Location = new Point(40, num),
			Width = 900
		});
		num += 34;
		shaderMarks = new StatusMark[8];
		shaderTexts = new Label[8];
		RoundCard roundCard3 = new RoundCard();
		roundCard3.Location = new Point(40, num);
		roundCard3.Size = new Size(900, 292);
		roundCard3.Flat = true;
		roundCard3.BackColor = Theme.Back;
		RoundCard roundCard4 = roundCard3;
		Font font = Theme.Ui(9.5f);
		for (int num2 = 0; num2 < 8; num2++)
		{
			shaderMarks[num2] = new StatusMark
			{
				Location = new Point(18, 12 + num2 * 34 + 2),
				Size = new Size(22, 22),
				Kind = 4
			};
			shaderTexts[num2] = new Label
			{
				Location = new Point(52, 10 + num2 * 34),
				Size = new Size(830, 26),
				Font = font,
				ForeColor = Theme.Text,
				BackColor = Theme.Back,
				AutoEllipsis = true,
				UseMnemonic = false,
				TextAlign = ContentAlignment.MiddleLeft
			};
			roundCard4.Controls.Add(shaderMarks[num2]);
			roundCard4.Controls.Add(shaderTexts[num2]);
		}
		shaderTexts[0].Text = L("Reading the cache...", "A ler a cache...", "Lecture du cache...");
		page.Controls.Add(roundCard4);
		num += roundCard4.Height + 12;
		RoundCard roundCard5 = new RoundCard();
		roundCard5.Location = new Point(40, num);
		roundCard5.Size = new Size(900, 112);
		roundCard5.Flat = true;
		roundCard5.BackColor = Theme.Back;
		RoundCard roundCard6 = roundCard5;
		shaderBar = new LoadBar
		{
			Location = new Point(18, 14),
			Size = new Size(864, 36),
			BackColor = Theme.Back
		};
		pipeBar = new LoadBar
		{
			Location = new Point(18, 62),
			Size = new Size(864, 36),
			BackColor = Theme.Back
		};
		roundCard6.Controls.Add(shaderBar);
		roundCard6.Controls.Add(pipeBar);
		page.Controls.Add(roundCard6);
		num += roundCard6.Height + 14;
		Pill pill = new Pill();
		pill.Text = L("Repair", "Reparar", "Réparer");
		pill.Size = new Size(130, 38);
		Pill pill2 = pill;
		pill2.Click += (object s, EventArgs e) =>
		{
			RepairShaders(all: false);
		};
		Pill pill3 = new Pill();
		pill3.Text = L("Rebuild everything", "Refazer tudo", "Tout refaire");
		pill3.Size = new Size(180, 38);
		Pill pill4 = pill3;
		pill4.Click += (object s, EventArgs e) =>
		{
			RepairShaders(all: true);
		};
		Pill pill5 = new Pill();
		pill5.Text = L("Open the cache folder", "Abrir a pasta da cache", "Ouvrir le dossier du cache");
		pill5.Size = new Size(200, 38);
		Pill pill6 = pill5;
		pill6.Click += (object s, EventArgs e) =>
		{
			Directory.CreateDirectory(ActiveCacheDir);
			OpenUrl(ActiveCacheDir);
		};
		num = Kit.Row(page, 40, num, 900, pill2, pill4, pill6) + 14;
		Label label = Kit.Text(L("Repair: makes the missing shaders and retries the pipelines that failed. Rebuild everything: clears the cache and prepares it again (about 5 minutes).", "Reparar: faz os shaders que faltam e tenta de novo as pipelines que falharam. Refazer tudo: limpa a cache e prepara-a de novo (cerca de 5 minutos).", "Réparer : fait les shaders manquants et réessaie les pipelines qui ont échoué. Tout refaire : vide le cache et le prépare à nouveau (environ 5 minutes)."), Theme.Ui(9f), Theme.Dim, Theme.Back, 40, num, 900);
		page.Controls.Add(label);
		num += label.Height + 20;
		num = StorageSection(page, 40, num, 900);
		num += 6;
		page.Controls.Add(new Heading(L("Last errors (prepare.log)", "Ultimos erros (prepare.log)", "Dernières erreurs (prepare.log)"))
		{
			Location = new Point(40, num),
			Width = 900
		});
		num += 34;
		shaderErrors = new TextBox
		{
			Multiline = true,
			ReadOnly = true,
			ForeColor = Theme.Dim,
			Font = Theme.Mono(8.5f),
			ScrollBars = ScrollBars.Vertical,
			BorderStyle = BorderStyle.None,
			BackColor = Theme.Back,
			TabStop = false
		};
		RoundCard roundCard7 = new RoundCard();
		roundCard7.Location = new Point(40, num);
		roundCard7.Size = new Size(900, 170);
		roundCard7.Flat = true;
		roundCard7.BackColor = Theme.Back;
		RoundCard roundCard8 = roundCard7;
		shaderErrors.Location = new Point(16, 14);
		shaderErrors.Size = new Size(868, roundCard8.Height - 28);
		Kit.Dark(shaderErrors, "DarkMode_Explorer");
		roundCard8.Controls.Add(shaderErrors);
		page.Controls.Add(roundCard8);
		page.Controls.Add(new Label
		{
			Location = new Point(40, num + roundCard8.Height + 10),
			Size = new Size(10, 14),
			BackColor = Theme.Back
		});
		shaderTimer = new System.Windows.Forms.Timer
		{
			Interval = 1000
		};
		shaderTimer.Tick += (object s, EventArgs e) =>
		{
			if (page.Visible)
			{
				RefreshShaders();
			}
		};
		shaderTimer.Start();
		return page;
	}

	private void Stage(int stage, float part, string text, bool live)
	{
		if (InvokeRequired)
		{
			try
			{
				BeginInvoke(new Action<int, float, string, bool>(Stage), stage, part, text, live);
				return;
			}
			catch (Exception)
			{
				return;
			}
		}
		stageLive[stage] = live;
		LoadBar loadBar = ((stage == 0) ? shaderBar : pipeBar);
		loadBar?.Set(part, Kit.Clip(text, loadBar.Font, Math.Max(80, loadBar.Width - 70)));
	}

	private static int Lines(string file)
	{
		try
		{
			return File.Exists(file) ? File.ReadLines(file).Count((string l) => l.Trim().Length > 0) : 0;
		}
		catch (Exception)
		{
			return 0;
		}
	}

	private void RefreshShaders()
	{
		if (shaderTexts == null || shaderReading)
		{
			return;
		}
		shaderReading = true;
		string cache = ActiveCacheDir;
		string log = Path.Combine(dataRoot, "logs", "prepare.log");
		Thread thread = new Thread(() =>
		{
			List<Tuple<int, string>> rows = new List<Tuple<int, string>>();
			string errors = "";
			int shaders = 0;
			int done = 0;
			int num = 0;
			int used = 0;
			bool prepared = false;
			try
			{
				long num2 = 0L;
				string path = Path.Combine(cache, "shaders");
				if (Directory.Exists(path))
				{
					foreach (FileInfo item4 in new DirectoryInfo(path).EnumerateFiles("*", System.IO.SearchOption.AllDirectories))
					{
						shaders++;
						num2 += item4.Length;
					}
				}
				string path2 = Path.Combine(cache, "pipelines");
				done = Lines(Path.Combine(path2, "warm-done.txt"));
				num = Lines(Path.Combine(path2, "warm-bad.txt"));
				used = Lines(Path.Combine(path2, "early-pipelines.txt"));
				long num3 = -1L;
				try
				{
					Directory.CreateDirectory(cache);
					num3 = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(cache))).AvailableFreeSpace;
				}
				catch (Exception)
				{
				}
				prepared = ReadSetting("shaders") == "1";
				bool flag = cache.All((char c) => c < '\u0080');
				rows.Add(Tuple.Create((shaders >= 1000) ? 1 : 0, string.Format(L("Shaders translated: {0:N0}  ({1:0.0} GB)", "Shaders traduzidos: {0:N0}  ({1:0.0} GB)"), shaders, (double)num2 / 1073741824.0)));
				List<Tuple<int, string>> list = rows;
				int item = ((prepared || shaders >= 1000) ? 1 : 0);
				string item2 = (prepared ? L("Last preparation finished well", "A ultima preparacao acabou bem") : ((shaders >= 1000) ? L("Prepared (by another way of starting VIctory, or before this launcher)", "Preparado (por outra forma de arrancar o VIctory, ou antes deste launcher)") : L("Not prepared yet, or not since the last update (press Prepare shaders)", "Ainda nao preparado, ou nao desde o ultimo update (carrega em Preparar shaders)")));
				list.Add(Tuple.Create(item, item2));
				rows.Add(Tuple.Create((done > 0) ? 1 : (-1), string.Format(L("Pipelines made for the driver: {0:N0}  (used by your sessions: {1:N0})", "Pipelines feitas para o driver: {0:N0}  (usadas nas tuas sessoes: {1:N0})"), done, used)));
				rows.Add(Tuple.Create((num == 0) ? 1 : 0, string.Format(L("Pipelines that failed: {0:N0}", "Pipelines que falharam: {0:N0}"), num) + ((num > 0) ? L("  (Repair tries them again)", "  (Reparar tenta de novo)") : "")));
				string text = ReadSetting("shaders_driver") ?? "";
				rows.Add(Tuple.Create((text.Length == 0 || text == driverVersion) ? 1 : (-1), (driverVersion.Length == 0) ? L("Graphics driver: not read", "Driver grafico: nao lido") : ((text.Length > 0 && text != driverVersion) ? string.Format(L("Driver changed ({0} → {1}): PLAY remakes the pipelines once", "Driver mudou ({0} → {1}): o JOGAR refaz as pipelines uma vez"), text, driverVersion) : string.Format(L("Graphics driver {0}", "Driver grafico {0}"), driverVersion))));
				rows.Add(Tuple.Create((num3 < 0) ? (-1) : ((num3 >= 12884901888L) ? 1 : 0), (num3 < 0) ? L("Disk room: not read", "Espaco em disco: nao lido") : string.Format(L("Free on the cache's disk: {0:0.0} GB  (12 GB needed to prepare)", "Livre no disco da cache: {0:0.0} GB  (12 GB para preparar)"), (double)num3 / 1073741824.0)));
				rows.Add(Tuple.Create(flag ? 1 : 0, flag ? L("Cache path is plain (no accents or symbols)", "Caminho da cache simples (sem acentos nem simbolos)") : L("The cache path has accents or symbols: move it (launcher.ini cache=)", "O caminho da cache tem acentos ou simbolos: muda-o (launcher.ini cache=)")));
				rows.Add(Tuple.Create(-1, L("Cache: ", "Cache: ") + Scrub(cache) + "  ·  " + EngineName));
				if (File.Exists(log))
				{
					using FileStream fileStream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
					if (fileStream.Length > 4194304)
					{
						fileStream.Seek(-4194304L, SeekOrigin.End);
					}
					IEnumerable<string> source = (from s in new StreamReader(fileStream).ReadToEnd().Split('\n')
						select s.TrimEnd('\r') into s
						where Regex.IsMatch(s, "(?i)\\b(error|panicked|failed|denied|not found|no space|do not translate)\\b")
						select s).Reverse().Take(30).Reverse();
					errors = string.Join("\r\n", source.ToArray());
				}
			}
			catch (Exception)
			{
			}
			try
			{
				BeginInvoke((Action)(() =>
				{
					for (int i = 0; i < shaderTexts.Length && i < rows.Count; i++)
					{
						int item3 = rows[i].Item1;
						shaderMarks[i].Set(item3 switch
						{
							0 => 0, 
							1 => 1, 
							_ => 3, 
						}, "");
						if (shaderTexts[i].Text != rows[i].Item2)
						{
							shaderTexts[i].Text = rows[i].Item2;
							shaderTips.SetToolTip(shaderTexts[i], rows[i].Item2);
						}
					}
					string text2 = ((errors.Length == 0) ? L("(no errors)", "(sem erros)") : Scrub(errors));
					if (shaderErrors.Text != text2)
					{
						shaderErrors.Text = text2;
					}
					shaderErrors.ForeColor = ((errors.Length == 0) ? Theme.Dim : Theme.Soft);
					if (!stageLive[0])
					{
						shaderBar.Set((shaders >= 1000 || prepared) ? 1f : 0f, Kit.Clip((shaders >= 1000 || prepared) ? string.Format(L("Shaders: {0:N0} translated, ready", "Shaders: {0:N0} traduzidos, prontos", "Shaders : {0:N0} traduits, prêts"), shaders) : L("Shaders: not prepared yet (press Prepare shaders)", "Shaders: ainda nao preparados (carrega em Preparar shaders)", "Shaders : pas encore préparés (appuie sur Préparer les shaders)"), shaderBar.Font, Math.Max(80, shaderBar.Width - 70)));
					}
					if (!stageLive[1])
					{
						pipeBar.Set((used > 0) ? Math.Min(1f, (float)done / (float)used) : ((done > 0) ? 1f : 0f), Kit.Clip(string.Format(L("Pipelines: {0:N0} made for the driver (your sessions used {1:N0})", "Pipelines: {0:N0} feitas para o driver (as tuas sessoes usaram {1:N0})", "Pipelines : {0:N0} faites pour le pilote (tes sessions en ont utilisé {1:N0})"), done, used), pipeBar.Font, Math.Max(80, pipeBar.Width - 70)));
					}
					shaderReading = false;
				}));
			}
			catch (Exception)
			{
				shaderReading = false;
			}
		});
		thread.IsBackground = true;
		thread.Start();
	}

	private void RepairShaders(bool all)
	{
		if (State() != GameState.None)
		{
			MessageBox.Show(this, L("Close the game first.", "Fecha o jogo primeiro."), "Julia's Launcher");
			return;
		}
		string activeCacheDir = ActiveCacheDir;
		try
		{
			if (all)
			{
				if (MessageBox.Show(this, L("Delete the shader cache and prepare it again (about 5 minutes)?", "Apagar a cache de shaders e prepara-la de novo (cerca de 5 minutos)?"), "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
				{
					return;
				}
				string[] array = new string[2] { "shaders", "pipelines" };
				foreach (string path in array)
				{
					string path2 = Path.Combine(activeCacheDir, path);
					if (Directory.Exists(path2))
					{
						Directory.Delete(path2, recursive: true);
					}
				}
				WriteSetting("shaders", "0");
				Log(L("Shader cache cleared.", "Cache de shaders limpa."));
			}
			else
			{
				string path3 = Path.Combine(activeCacheDir, "pipelines", "warm-bad.txt");
				if (File.Exists(path3))
				{
					File.Delete(path3);
				}
			}
		}
		catch (Exception ex)
		{
			Log(L("Could not clear the cache: ", "Nao deu para limpar a cache: ") + ex.Message);
			return;
		}
		Prepare();
	}

	private void AskSessionLogs()
	{
		if (ReadSetting("share_logs") == null)
		{
			Form f = new Form
			{
				Text = L("Game logs", "Logs do jogo", "Logs du jeu"),
				ClientSize = new Size(660, 470),
				BackColor = Theme.Back,
				ForeColor = Theme.Text,
				Font = Theme.Ui(9.75f),
				StartPosition = FormStartPosition.CenterParent,
				FormBorderStyle = FormBorderStyle.FixedDialog,
				MaximizeBox = false,
				MinimizeBox = false,
				Icon = Icon
			};
			f.Controls.Add(new Heading(L("New: send a log of each game?", "Novo: enviar uma log de cada jogo?", "Nouveau : envoyer un log de chaque partie ?"))
			{
				Location = new Point(20, 16),
				Width = 620
			});
			Label label = new Label();
			label.Location = new Point(20, 50);
			label.Size = new Size(620, 340);
			label.ForeColor = Theme.Text;
			label.BackColor = Theme.Back;
			Label label2 = label;
			Label label3 = label2;
			string[] value;
			if (Pt)
			{
				value = new string[15]
				{
					"Quando o jogo fecha, o Julia's Launcher pode enviar ao edupode uma log padrao dessa sessao.", "", "PORQUE AJUDA", "   As logs mostram onde o jogo fica lento, que shaders falham e porque fecha, em PCs que nao", "   temos. E assim que as proximas otimizacoes sao escolhidas.", "", "O QUE E ENVIADO, CADA VEZ QUE O JOGO FECHA", "   Um resumo (placa grafica, processador, RAM, Windows, predefinicao e opcoes, quanto tempo", "   correu, como acabou) e a parte final da log do jogo (cerca de 256 KB).", "   O teu nome de utilizador e tirado de todos os caminhos. So o edupode le as logs.",
					"", "NUNCA E ENVIADO", "   Os teus ficheiros, os teus saves, o teu IP.", "", "Podes mudar quando quiseres em Hardware e ID."
				};
			}
			else
			{
				value = (Fr ? new string[15]
				{
					"Quand le jeu se ferme, Julia's Launcher peut envoyer à edupode un log standard de la partie.", "", "POURQUOI ÇA AIDE", "   Les logs montrent où le jeu ralentit, quels shaders échouent et pourquoi il se ferme, sur des PC", "   que nous n'avons pas. C'est ainsi que les prochaines optimisations sont choisies.", "", "CE QUI EST ENVOYÉ, À CHAQUE FERMETURE DU JEU", "   Un résumé (carte graphique, processeur, RAM, Windows, préréglage et options, durée de la", "   partie, comment elle a fini) et la fin du log du jeu (environ 256 Ko).", "   Ton nom d'utilisateur est retiré de tous les chemins. Seul edupode lit les logs.",
					"", "JAMAIS ENVOYÉ", "   Tes fichiers, tes sauvegardes, ton adresse IP.", "", "Tu peux changer d'avis à tout moment dans Matériel et ID."
				} : new string[15]
				{
					"When the game closes, Julia's Launcher can send edupode a standard log of that game.", "", "WHY IT HELPS", "   Logs show where the game slows down, which shaders fail and why it closes, on PCs we do not", "   have. That is how the next optimizations are chosen.", "", "WHAT IS SENT, EACH TIME THE GAME CLOSES", "   A summary (graphics card, processor, RAM, Windows, preset and options, how long the game ran,", "   how it ended) and the last part of the game's log (about 256 KB).", "   Your user name is taken out of every path. Only edupode reads the logs.",
					"", "NEVER SENT", "   Your files, your saves, your IP address.", "", "You can change it any time on Hardware & ID."
				});
			}
			label3.Text = string.Join("\r\n", value);
			f.Controls.Add(label2);
			Pill pill = new Pill();
			pill.Main = true;
			pill.Text = L("Yes, send game logs", "Sim, enviar as logs", "Oui, envoyer les logs");
			pill.Location = new Point(20, 408);
			pill.Size = new Size(250, 42);
			Pill pill2 = pill;
			Pill pill3 = new Pill();
			pill3.Text = L("No", "Nao", "Non");
			pill3.Location = new Point(280, 412);
			pill3.Size = new Size(140, 34);
			Pill pill4 = pill3;
			pill2.Click += (object s, EventArgs e) =>
			{
				SetSessionLogs(on: true);
				f.Close();
			};
			pill4.Click += (object s, EventArgs e) =>
			{
				SetSessionLogs(on: false);
				f.Close();
			};
			f.Controls.Add(pill2);
			f.Controls.Add(pill4);
			f.ShowDialog(this);
		}
	}

	private void SetSessionLogs(bool on)
	{
		WriteSetting("share_logs", on ? "1" : "0");
		if (logsBox != null && logsBox.Checked != on)
		{
			logsBox.Checked = on;
		}
		Log(on ? L("Game logs: one is sent to edupode when each game closes. Thank you!", "Logs do jogo: uma e enviada ao edupode quando cada jogo fecha. Obrigado!", "Logs du jeu : un est envoyé à edupode à la fin de chaque partie. Merci !") : L("Game logs are not sent.", "As logs do jogo nao sao enviadas.", "Les logs du jeu ne sont pas envoyés."));
	}

	private int SessionLogsSection(Panel page, int X, int y, int W)
	{
		logsBox = new OptionCard
		{
			Text = L("Send a log of each game to edupode", "Enviar uma log de cada jogo ao edupode", "Envoyer un log de chaque partie à edupode"),
			Line = L("When the game closes: a summary and the end of its log, your user name taken out.", "Quando o jogo fecha: um resumo e o fim da log, sem o teu nome de utilizador.", "À la fermeture du jeu : un résumé et la fin du log, sans ton nom d'utilisateur."),
			Location = new Point(X, y),
			Size = new Size(W, 58),
			Checked = SessionLogsOn
		};
		logsBox.CheckedChanged += (object s, EventArgs e) =>
		{
			if (logsBox.Checked != SessionLogsOn)
			{
				SetSessionLogs(logsBox.Checked);
			}
		};
		page.Controls.Add(logsBox);
		return y + 66;
	}

	private void SendSessionLog(string log, int code, DateTime started, DateTime ended)
	{
		if (!SessionLogsOn)
		{
			return;
		}
		string summary;
		try
		{
			summary = SessionSummary(code, started, ended);
		}
		catch (Exception)
		{
			return;
		}
		HardwareInfo hwNow = hw;
		Thread thread = new Thread(() =>
		{
			try
			{
				string id = (hwNow ?? ReadHardware()).Id;
				byte[] array;
				using (MemoryStream memoryStream = new MemoryStream())
				{
					using (ZipArchive z = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
					{
						Put(z, "summary.txt", Encoding.UTF8.GetBytes(summary));
						Put(z, Path.GetFileName(log), Encoding.UTF8.GetBytes(Scrub(Tail(log, 262144))));
					}
					array = memoryStream.ToArray();
				}
				if (array.Length <= 1843200)
				{
					ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
					HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create("https://vi-hardware.edupodept.workers.dev/v1/report");
					httpWebRequest.Method = "POST";
					httpWebRequest.ContentType = "application/zip";
					httpWebRequest.UserAgent = "Julias-Launcher";
					httpWebRequest.Timeout = 60000;
					httpWebRequest.Headers["X-VI-Id"] = id;
					httpWebRequest.Headers["X-VI-Kind"] = "session";
					httpWebRequest.ContentLength = array.Length;
					using (Stream stream = httpWebRequest.GetRequestStream())
					{
						stream.Write(array, 0, array.Length);
					}
					using ((HttpWebResponse)httpWebRequest.GetResponse())
					{
					}
					SafeLog(L("Game log sent to edupode.", "Log do jogo enviada ao edupode.", "Log du jeu envoyé à edupode."));
				}
			}
			catch (Exception)
			{
			}
		});
		thread.IsBackground = true;
		thread.Start();
	}

	private static void Put(ZipArchive z, string name, byte[] bytes)
	{
		ZipArchiveEntry zipArchiveEntry = z.CreateEntry(name, CompressionLevel.Optimal);
		using Stream stream = zipArchiveEntry.Open();
		stream.Write(bytes, 0, bytes.Length);
	}

	private static string Tail(string file, int bytes)
	{
		try
		{
			using FileStream fileStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			if (fileStream.Length > bytes)
			{
				fileStream.Seek(-bytes, SeekOrigin.End);
			}
			return new StreamReader(fileStream).ReadToEnd();
		}
		catch (Exception)
		{
			return "";
		}
	}

	private string SessionSummary(int code, DateTime started, DateTime ended)
	{
		HardwareInfo hardwareInfo = hw ?? ReadHardware();
		string options = "";
		Invoke((Action)(() =>
		{
			options = string.Join("\r\n", (from kv in Environment2()
				where kv.Key != "VICTORY_GAME_DIR"
				select kv.Key + "=" + kv.Value).ToArray());
		}));
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Julia's Launcher game log");
		stringBuilder.AppendLine("ID: " + hardwareInfo.Id);
		stringBuilder.AppendLine("Kind: session");
		stringBuilder.AppendLine("Launcher: v0.3.16   Engine: " + EngineName + "   Preset: " + Selected.Name + "   Output: " + OutputHeight + "p");
		stringBuilder.AppendLine("GPU: " + hardwareInfo.Gpu + " (" + hardwareInfo.VramMb / 1024 + " GB, driver " + hardwareInfo.Driver + ")   " + RebarText() + "   VIctory runs on: " + probeAdapter);
		stringBuilder.AppendLine("CPU: " + hardwareInfo.Cpu + "   RAM: " + hardwareInfo.RamGb + " GB   Board: " + hardwareInfo.BoardMaker + " " + hardwareInfo.Board);
		stringBuilder.AppendLine("Windows: " + hardwareInfo.Os + "   Disk: " + hardwareInfo.Disk + (hardwareInfo.Laptop ? "   laptop" : ""));
		stringBuilder.AppendLine("Shaders cached: " + shaderFiles);
		stringBuilder.AppendLine(string.Format("Played: {0:0.0} min   Ended: {1}", (ended - started).TotalMinutes, (code == 0) ? "normally (code 0)" : ("with an error (code " + code + ")")));
		return Scrub(stringBuilder.ToString()) + "\r\nOptions:\r\n" + Scrub(options);
	}

	private void ShowWhatsNewOnce()
	{
		if (!(ReadSetting("seen_version") == "0.3.16"))
		{
			WriteSetting("seen_version", "0.3.16");
			ShowWhatsNew();
		}
	}

	private void ShowWhatsNew()
	{
		Form f = Dialog(L("What's new", "Novidades", "Nouveautés") + " · v0.3.16", 800, 660);
		f.Controls.Add(new Heading(string.Format(L("What's new in v{0}", "Novidades da v{0}", "Nouveautés de la v{0}"), "0.3.16"))
		{
			Location = new Point(24, 18),
			Width = 750
		});
		RoundCard roundCard = new RoundCard();
		roundCard.Location = new Point(24, 58);
		roundCard.Size = new Size(752, 480);
		RoundCard roundCard2 = roundCard;
		Panel scroll = new Panel
		{
			Location = new Point(8, 8),
			Size = new Size(roundCard2.Width - 16, roundCard2.Height - 16),
			AutoScroll = true,
			BackColor = Theme.Card
		};
		Kit.Dark(scroll, "DarkMode_Explorer");
		roundCard2.Controls.Add(scroll);
		f.Controls.Add(roundCard2);
		int textW = scroll.Width - SystemInformation.VerticalScrollBarWidth - 24;
		int y = 8;
		Regex regex = new Regex("\\d+\\.\\d+\\.\\d+\\s*$");
		string current = null;
		bool bullet = false;
		Action action = () =>
		{
			if (current != null)
			{
				string text4 = current.Trim();
				current = null;
				if (text4.Length != 0)
				{
					Font font = Theme.Ui(9.75f);
					int num4 = (bullet ? 34 : 14);
					if (bullet)
					{
						scroll.Controls.Add(new Label
						{
							Text = "•",
							Font = font,
							ForeColor = Theme.Cyan,
							BackColor = Theme.Card,
							AutoSize = false,
							Location = new Point(16, y),
							Size = new Size(16, 22),
							UseMnemonic = false
						});
					}
					Label label3 = Kit.Text(text4, font, bullet ? Theme.Text : Theme.Soft, Theme.Card, num4, y, textW - num4);
					scroll.Controls.Add(label3);
					y += label3.Height + 6;
				}
			}
		};
		string[] array;
		if (Fr)
		{
			array = NewFr;
		}
		else
		{
			array = (Pt ? NewPt : NewEn);
		}
		foreach (string text in array)
		{
			string text2 = text.TrimEnd();
			string text3 = text2.TrimStart();
			int num2 = text2.Length - text3.Length;
			if (text3.Length == 0)
			{
				action();
				y += 4;
				continue;
			}
			if (text3.StartsWith("•"))
			{
				action();
				current = text3.Substring(1).Trim();
				bullet = true;
				continue;
			}
			if (num2 > 0)
			{
				if (current == null)
				{
					bullet = false;
				}
				current = ((current == null) ? text3 : (current + " " + text3));
				continue;
			}
			action();
			if (regex.IsMatch(text3))
			{
				y += 8;
				Font f2 = Theme.UiBold(12f);
				Label label = Kit.Text(text3, f2, Theme.Cyan, Theme.Card, 14, y, textW - 14);
				scroll.Controls.Add(label);
				y += label.Height + 6;
			}
			else
			{
				Font f3 = Theme.UiBold(9f);
				Label label2 = Kit.Text(text3, f3, Theme.Peach, Theme.Card, 14, y + 2, textW - 14);
				scroll.Controls.Add(label2);
				y += label2.Height + 8;
			}
		}
		action();
		scroll.Controls.Add(new Label
		{
			Location = new Point(0, y + 8),
			Size = new Size(10, 6),
			BackColor = Theme.Card
		});
		Pill pill = new Pill();
		pill.Main = true;
		pill.Text = L("Got it", "Percebi", "Compris");
		pill.Size = new Size(160, 42);
		Pill pill2 = pill;
		pill2.Click += (object s, EventArgs e) =>
		{
			f.Close();
		};
		Pill pill3 = new Pill();
		pill3.Text = L("Is it safe?", "E seguro?", "Est-ce sûr ?");
		pill3.Size = new Size(160, 38);
		Pill pill4 = pill3;
		pill4.Click += (object s, EventArgs e) =>
		{
			OpenUrl("https://github.com/felicienradio/cold-project-vi/blob/main/docs/SAFETY.md");
		};
		Pill pill5 = new Pill();
		pill5.Text = L("All releases", "Todas as versoes", "Toutes les versions");
		pill5.Size = new Size(170, 38);
		Pill pill6 = pill5;
		pill6.Click += (object s, EventArgs e) =>
		{
			OpenUrl("https://github.com/felicienradio/cold-project-vi/releases");
		};
		int num3 = roundCard2.Bottom + 16;
		pill4.Size = new Size(Math.Max(pill4.Width, Kit.PillW(pill4.Text, 9.5f, 140)), 38);
		pill6.Size = new Size(Math.Max(pill6.Width, Kit.PillW(pill6.Text, 9.5f, 140)), 38);
		pill4.Location = new Point(24, num3 + 2);
		pill6.Location = new Point(pill4.Right + 10, num3 + 2);
		pill2.Location = new Point(776 - pill2.Width, num3);
		f.Controls.Add(pill2);
		f.Controls.Add(pill4);
		f.Controls.Add(pill6);
		f.ClientSize = new Size(800, num3 + 42 + 22);
		f.AcceptButton = pill2;
		f.ShowDialog(this);
	}

	private static void GrowTo(Control c, int height)
	{
		int num = height - c.Height;
		if (num == 0 || c.Parent == null)
		{
			return;
		}
		int top = c.Top;
		c.Height = height;
		foreach (Control control in c.Parent.Controls)
		{
			if (control != c && control.Top > top)
			{
				control.Top += num;
			}
		}
	}

	private static void FitLabel(Label l)
	{
		int num = TextRenderer.MeasureText(l.Text ?? "", l.Font, new Size(l.Width, 0), TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak).Height + 4;
		if (num > l.Height)
		{
			GrowTo(l, num);
		}
	}

	private int ReferencePcSection(Panel page, int X, int y, int W)
	{
		page.Controls.Add(new Heading(L("Reference PC (the presets are measured on it)", "PC de referencia (as predefinicoes sao medidas nele)", "PC de référence (les préréglages sont mesurés dessus)"))
		{
			Location = new Point(X, y),
			Width = W
		});
		y += 32;
		string[] value = new string[5]
		{
			L("Laptop:  Acer Predator Helios Neo 16 (PHN16-73), plugged in, performance mode", "Portatil:  Acer Predator Helios Neo 16 (PHN16-73), ligado a corrente, modo desempenho", "Portable :  Acer Predator Helios Neo 16 (PHN16-73), sur secteur, mode performance"),
			L("Graphics: NVIDIA GeForce RTX 5070 Ti Laptop GPU, 12 GB, Resizable BAR on", "Grafica:  NVIDIA GeForce RTX 5070 Ti Laptop GPU, 12 GB, Resizable BAR ligado", "Graphique : NVIDIA GeForce RTX 5070 Ti Laptop GPU, 12 Go, Resizable BAR activé"),
			L("CPU:      Intel Core Ultra 9 275HX (24 cores)", "CPU:      Intel Core Ultra 9 275HX (24 nucleos)", "CPU :      Intel Core Ultra 9 275HX (24 cœurs)"),
			L("Memory:   32 GB RAM, 1 TB NVMe SSD, Windows 11", "Memoria:  32 GB de RAM, SSD NVMe de 1 TB, Windows 11", "Mémoire :  32 Go de RAM, SSD NVMe de 1 To, Windows 11"),
			L("Recommended (720p → 1080p): about 22-26 FPS in the open world, 35+ in cutscenes, 6-8 GB of video memory, ~20 GB of RAM.", "Recomendado (720p → 1080p): cerca de 22-26 FPS no mundo aberto, 35+ nas cutscenes, 6-8 GB de VRAM, ~20 GB de RAM.", "Recommandé (720p → 1080p) : environ 22-26 FPS en monde ouvert, 35+ en cinématiques, 6-8 Go de VRAM, ~20 Go de RAM.")
		};
		InfoCard infoCard = new InfoCard();
		infoCard.Location = new Point(X, y);
		infoCard.Size = new Size(W, 200);
		infoCard.Text = string.Join("\r\n", value);
		InfoCard infoCard2 = infoCard;
		infoCard2.Height = infoCard2.Fit();
		page.Controls.Add(infoCard2);
		return y + infoCard2.Height + 16;
	}

	private int PlayersPcsSection(Panel page, int X, int y, int W)
	{
		page.Controls.Add(new Heading(L("Players' PCs (updated every 6 hours)", "PCs dos jogadores (atualizado de 6 em 6 horas)", "PC des joueurs (mis à jour toutes les 6 heures)"))
		{
			Location = new Point(X, y),
			Width = W
		});
		y += 32;
		Label head = new Label
		{
			Location = new Point(X, y),
			Size = new Size(W - 170, 40),
			ForeColor = Theme.Dim,
			BackColor = Theme.Back,
			Font = Theme.Ui(9f),
			Text = L("Reading the averages from GitHub...", "A ler as medias do GitHub...", "Lecture des moyennes sur GitHub...")
		};
		page.Controls.Add(head);
		Pill pill = new Pill();
		pill.Text = L("Open on GitHub", "Abrir no GitHub", "Ouvrir sur GitHub");
		pill.Location = new Point(X + W - 160, y);
		pill.Size = new Size(160, 32);
		Pill pill2 = pill;
		pill2.Click += (object s, EventArgs e) =>
		{
			OpenUrl("https://github.com/felicienradio/cold-project-vi/blob/main/docs/HARDWARE-STATS.md");
		};
		page.Controls.Add(pill2);
		y += 46;
		BarChart.YouText = L("you", "tu", "toi");
		StatsTable table = new StatsTable
		{
			Location = new Point(X, y),
			Size = new Size(W, 72),
			Empty = "...",
			Headers = new string[4]
			{
				L("Graphics card", "Placa grafica", "Carte graphique"),
				L("PCs", "PCs", "PC"),
				L("Video memory", "VRAM", "VRAM"),
				L("Resizable BAR for the game", "Resizable BAR para o jogo", "Resizable BAR pour le jeu")
			},
			Parts = new float[4] { 4.2f, 1f, 1.6f, 2.6f },
			RightAlign = new bool[4] { false, true, true, true }
		};
		page.Controls.Add(table);
		y += table.Height + 16;
		List<Tuple<string, BarChart>> charts = new List<Tuple<string, BarChart>>();
		string[] array = new string[4]
		{
			L("Graphics card makers", "Fabricantes de placas", "Fabricants de cartes"),
			L("RAM", "RAM", "RAM"),
			L("Windows", "Windows", "Windows"),
			L("How many have it", "Quantos tem", "Combien l'ont")
		};
		foreach (string item in array)
		{
			BarChart barChart = new BarChart();
			barChart.Location = new Point(X, y + 26);
			barChart.Size = new Size(W, 60);
			barChart.Empty = "...";
			BarChart barChart2 = barChart;
			Label label = new Label();
			label.Location = new Point(X, y);
			label.Size = new Size(W, 22);
			label.Text = item;
			label.ForeColor = Theme.Text;
			label.BackColor = Theme.Back;
			label.Font = Theme.UiBold(9.5f);
			Label value = label;
			page.Controls.Add(value);
			page.Controls.Add(barChart2);
			charts.Add(Tuple.Create(item, barChart2));
			y += 102;
		}
		int result = y;
		Thread thread = new Thread(() =>
		{
			Dictionary<string, object> s = null;
			try
			{
				ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
				using WebClient webClient = new WebClient();
				webClient.Headers[HttpRequestHeader.UserAgent] = "Julias-Launcher";
				s = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(webClient.DownloadData("https://raw.githubusercontent.com/felicienradio/cold-project-vi/main/docs/hardware-stats.json"))) as Dictionary<string, object>;
			}
			catch (Exception)
			{
			}
			try
			{
				BeginInvoke((Action)(() =>
				{
					try
					{
						FillStats(s, head, table, charts);
					}
					catch (Exception)
					{
						if (s != null)
						{
							FillStats(null, head, table, charts);
						}
					}
				}));
			}
			catch (Exception)
			{
			}
		});
		thread.IsBackground = true;
		thread.Start();
		return result;
	}

	private static double Num(Dictionary<string, object> d, string k)
	{
		if (d == null || !d.TryGetValue(k, out var value) || value == null)
		{
			return 0.0;
		}
		return Convert.ToDouble(value);
	}

	private static Dictionary<string, object> Obj(Dictionary<string, object> d, string k)
	{
		if (d == null || !d.TryGetValue(k, out var value))
		{
			return null;
		}
		return value as Dictionary<string, object>;
	}

	private static IEnumerable<Dictionary<string, object>> Rows(Dictionary<string, object> d, string k)
	{
		object[] array = ((d != null && d.TryGetValue(k, out var value)) ? (value as object[]) : null);
		if (array != null)
		{
			return array.OfType<Dictionary<string, object>>();
		}
		return Enumerable.Empty<Dictionary<string, object>>();
	}

	private static string CardKey(string name)
	{
		string text = (name ?? "").ToLowerInvariant();
		string[] array = new string[5] { "amd ", "nvidia ", "geforce ", "intel(r) ", "intel " };
		foreach (string oldValue in array)
		{
			text = text.Replace(oldValue, "");
		}
		return text.Replace("(tm)", "").Trim();
	}

	private string MyVendor()
	{
		string text = (gpuName ?? "").ToLowerInvariant();
		if (text.Contains("radeon") || text.Contains("amd"))
		{
			return "amd";
		}
		if (text.Contains("geforce") || text.Contains("rtx") || text.Contains("gtx") || text.Contains("nvidia"))
		{
			return "nvidia";
		}
		if (text.Contains("intel") || text.Contains("arc"))
		{
			return "intel";
		}
		return "";
	}

	private void FillStats(Dictionary<string, object> s, Label head, StatsTable table, List<Tuple<string, BarChart>> charts)
	{
		if (s == null)
		{
			head.ForeColor = Theme.Warn;
			head.Text = L("GitHub could not be reached: the averages show when it can.", "Sem ligacao ao GitHub: as medias aparecem quando houver.", "GitHub injoignable : les moyennes s'afficheront plus tard.");
			FitLabel(head);
			string empty = (table.Empty = L("No data yet", "Sem dados ainda", "Pas encore de données"));
			table.Invalidate();
			{
				foreach (Tuple<string, BarChart> chart in charts)
				{
					chart.Item2.Empty = empty;
					chart.Item2.Invalidate();
				}
				return;
			}
		}
		head.ForeColor = Theme.Dim;
		Dictionary<string, object> d = Obj(s, "average");
		Dictionary<string, object> d2 = Obj(s, "percent");
		string text = ((s.TryGetValue("updated", out var value) && DateTime.TryParse(value as string, null, DateTimeStyles.RoundtripKind, out var result)) ? result.ToLocalTime().ToString("g") : "?");
		double pcs = Num(s, "pcs");
		head.Text = string.Format(L("{0:N0} PCs share their hardware. On average: {1:0.#} GB of RAM, {2:0.#} GB of video memory, {3:0} CPU threads. Updated {4}.", "{0:N0} PCs partilham o hardware. Em media: {1:0.#} GB de RAM, {2:0.#} GB de VRAM, {3:0} threads de CPU. Atualizado a {4}.", "{0:N0} PC partagent leur matériel. En moyenne : {1:0.#} Go de RAM, {2:0.#} Go de VRAM, {3:0} threads CPU. Mis à jour le {4}."), pcs, Num(d, "ram_gb"), Num(d, "vram_gb"), Num(d, "cpu_threads"), text);
		FitLabel(head);
		table.Empty = L("No data yet", "Sem dados ainda", "Pas encore de données");
		foreach (Tuple<string, BarChart> chart2 in charts)
		{
			chart2.Item2.Empty = table.Empty;
		}
		table.Rows.Clear();
		table.Highlight = -1;
		string text2 = CardKey(gpuName);
		foreach (Dictionary<string, object> item2 in Rows(s, "gpus"))
		{
			string text3 = item2["name"] as string;
			if (text3 == "other")
			{
				text3 = L("other (fewer than 3 PCs each)", "outras (menos de 3 PCs cada)", "autres (moins de 3 PC chacune)");
			}
			else if (text2.Length > 0 && CardKey(text3) == text2)
			{
				table.Highlight = table.Rows.Count;
			}
			table.Rows.Add(new string[4]
			{
				text3,
				Num(item2, "pcs").ToString("N0"),
				item2.ContainsKey("vram_gb") ? (Num(item2, "vram_gb").ToString("0") + " GB") : "",
				item2.ContainsKey("rebar_percent") ? (Num(item2, "rebar_percent").ToString("0") + " %") : ""
			});
		}
		GrowTo(table, table.Wanted);
		table.Invalidate();
		Func<string, List<Tuple<string, double, string>>> func = (string key) => (from r in Rows(s, key)
			select Tuple.Create((key == "ram_gb" && r["name"] as string != "other") ? string.Concat(r["name"], " GB") : (r["name"] as string), Num(r, "pcs"), Num(r, "pcs").ToString("N0") + "  (" + ((pcs > 0.0) ? (Num(r, "pcs") / pcs * 100.0) : 0.0).ToString("0") + " %)")).ToList();
		List<List<Tuple<string, double, string>>> list = new List<List<Tuple<string, double, string>>>();
		list.Add(func("gpu_vendors"));
		list.Add(func("ram_gb"));
		list.Add(func("windows"));
		list.Add(new List<Tuple<string, double, string>>
		{
			Tuple.Create(L("Resizable BAR for the game", "Resizable BAR para o jogo", "Resizable BAR pour le jeu"), Num(d2, "rebar_for_the_game"), Num(d2, "rebar_for_the_game").ToString("0") + " %"),
			Tuple.Create(L("Resizable BAR in the BIOS (NVIDIA)", "Resizable BAR na BIOS (NVIDIA)", "Resizable BAR dans le BIOS (NVIDIA)"), Num(d2, "rebar_in_bios"), Num(d2, "rebar_in_bios").ToString("0") + " %"),
			Tuple.Create(L("8 GB of video memory or less", "8 GB de VRAM ou menos", "8 Go de VRAM ou moins"), Num(d2, "vram_8_gb_or_less"), Num(d2, "vram_8_gb_or_less").ToString("0") + " %"),
			Tuple.Create(L("16 GB of RAM or less", "16 GB de RAM ou menos", "16 Go de RAM ou moins"), Num(d2, "ram_16_gb_or_less"), Num(d2, "ram_16_gb_or_less").ToString("0") + " %"),
			Tuple.Create(L("Laptops", "Portateis", "Portables"), Num(d2, "laptops"), Num(d2, "laptops").ToString("0") + " %"),
			Tuple.Create(L("SSD", "SSD", "SSD"), Num(d2, "ssd"), Num(d2, "ssd").ToString("0") + " %")
		});
		List<List<Tuple<string, double, string>>> list2 = list;
		string vendor = MyVendor();
		string ram = (int)Math.Round(ramGb) + " GB";
		string win = ((Environment.OSVersion.Version.Build >= 22000) ? "11" : "10");
		Func<string, bool>[] array = new Func<string, bool>[4]
		{
			(string n) => vendor.Length > 0 && (n ?? "").ToLowerInvariant().Contains(vendor),
			(string n) => ramGb > 0.0 && n == ram,
			(string n) => (n ?? "").Contains(win),
			null
		};
		for (int num = 0; num < charts.Count; num++)
		{
			BarChart item = charts[num].Item2;
			item.Items = list2[num];
			item.Mine = ((num < array.Length) ? array[num] : null);
			GrowTo(item, item.Wanted);
			item.Invalidate();
		}
	}

	private void CleanupOldLaunchers()
	{
		string fullPath = Path.GetFullPath(Application.ExecutablePath);
		try
		{
			string path = Path.Combine(root, "Julia's Launcher.old.exe");
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception)
		{
		}
		List<string> list = OlderCopies(fullPath);
		if (list.Count == 0)
		{
			return;
		}
		string text = string.Join("|", (from c in list
			select c.ToLowerInvariant() into c
			orderby c
			select c).ToArray()).GetHashCode().ToString("x");
		if (ReadSetting("cleanup_asked") == text)
		{
			return;
		}
		WriteSetting("cleanup_asked", text);
		string arg = string.Join("\r\n", list.Select((string c) => "   •  " + Scrub(c)).ToArray());
		string text2 = string.Format(L("Older copies of Julia's Launcher are on this PC:\r\n\r\n{0}\r\n\r\nMove them to the Recycle Bin? Your settings, shaders and saves stay (they are not in these files). From now on, use this one: v{1}.", "Ha copias antigas do Julia's Launcher neste PC:\r\n\r\n{0}\r\n\r\nMove-las para a Reciclagem? As tuas definicoes, shaders e saves ficam (nao estao nestes ficheiros). A partir de agora usa este: v{1}.", "Il y a d'anciennes copies de Julia's Launcher sur ce PC :\r\n\r\n{0}\r\n\r\nLes mettre à la Corbeille ? Tes réglages, shaders et sauvegardes restent (ils ne sont pas dans ces fichiers). Désormais, utilise celui-ci : v{1}."), arg, "0.3.16");
		if (MessageBox.Show(this, text2, "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			return;
		}
		int num = 0;
		foreach (string item in list)
		{
			try
			{
				FileSystem.DeleteFile(item, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
				num++;
			}
			catch (Exception)
			{
			}
		}
		Log(string.Format(L("{0} older launcher copies moved to the Recycle Bin.", "{0} copias antigas do launcher movidas para a Reciclagem.", "{0} anciennes copies du launcher mises à la Corbeille."), num));
	}

	private List<string> OlderCopies(string me)
	{
		List<string> list = new List<string>();
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string[] source = new string[3]
		{
			Path.Combine(folderPath, "Downloads"),
			Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			Environment.GetFolderPath(Environment.SpecialFolder.Personal)
		};
		Version version = Updater.Number("0.3.16");
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			Process[] processes = Process.GetProcesses();
			foreach (Process process in processes)
			{
				try
				{
					if (process.ProcessName.IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						hashSet.Add(process.MainModule.FileName);
					}
				}
				catch (Exception)
				{
				}
				finally
				{
					process.Dispose();
				}
			}
		}
		catch (Exception)
		{
		}
		foreach (string item in source.Distinct())
		{
			try
			{
				if (!Directory.Exists(item))
				{
					continue;
				}
				foreach (string item2 in Directory.EnumerateFiles(item, "*.exe", System.IO.SearchOption.TopDirectoryOnly))
				{
					string fileName = Path.GetFileName(item2);
					if (!fileName.StartsWith("Julia", StringComparison.OrdinalIgnoreCase) || fileName.IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) < 0)
					{
						continue;
					}
					string fullPath = Path.GetFullPath(item2);
					if (!string.Equals(fullPath, me, StringComparison.OrdinalIgnoreCase) && !hashSet.Contains(fullPath) && !File.Exists(Path.Combine(Path.GetDirectoryName(fullPath), "victory.exe")))
					{
						FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(fullPath);
						if ((string.IsNullOrEmpty(versionInfo.ProductName) || !(versionInfo.ProductName != "Julia's Launcher")) && !(Updater.Number(versionInfo.FileVersion ?? "0") >= version))
						{
							list.Add(fullPath);
						}
					}
				}
			}
			catch (Exception)
			{
			}
		}
		return list;
	}

	private string BuildTarget(string folder)
	{
		string storageRoot = StorageRoot;
		if (storageRoot != null)
		{
			return Path.Combine(storageRoot, "build", Path.GetFileName(folder.TrimEnd('\\')));
		}
		return Path.Combine(folder, "target");
	}

	private int StorageSection(Panel page, int X, int y, int W)
	{
		page.Controls.Add(new Heading(L("Where VIctory keeps its files", "Onde o VIctory guarda os ficheiros", "Où VIctory garde ses fichiers"))
		{
			Location = new Point(X, y),
			Width = W
		});
		y += 32;
		storageLabel = new Label
		{
			Location = new Point(X, y + 6),
			Size = new Size(W - 230, 40),
			ForeColor = Theme.Text,
			BackColor = Theme.Back,
			Font = Theme.Mono(9f),
			AutoEllipsis = true
		};
		page.Controls.Add(storageLabel);
		Pill pill = new Pill();
		pill.Text = L("Move to another drive...", "Mudar para outro disco...", "Déplacer vers un autre disque...");
		pill.Location = new Point(X + W - 220, y);
		pill.Size = new Size(220, 36);
		Pill pill2 = pill;
		pill2.Click += (object s, EventArgs e) =>
		{
			MoveStorage();
		};
		page.Controls.Add(pill2);
		y += 46;
		page.Controls.Add(new Label
		{
			Location = new Point(X, y),
			Size = new Size(W, 36),
			ForeColor = Theme.Dim,
			BackColor = Theme.Back,
			Font = Theme.Ui(9f),
			Text = L("The shader cache (10-20 GB), the game's data and saves, the AMD engine and builds from source. Your fastest SSD is best, even if Windows is not on it.", "A cache de shaders (10-20 GB), os dados e saves do jogo, o motor AMD e as compilacoes a partir do codigo. O teu SSD mais rapido e o melhor, mesmo sem o Windows nele.", "Le cache de shaders (10-20 Go), les données et sauvegardes du jeu, le moteur AMD et les compilations depuis le code. Ton SSD le plus rapide est le mieux, même sans Windows dessus.")
		});
		y += 42;
		ShowStorage();
		return y;
	}

	private void ShowStorage()
	{
		if (storageLabel != null)
		{
			string text = StorageRoot ?? Path.GetDirectoryName(dataRoot);
			long num = -1L;
			try
			{
				num = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(text))).AvailableFreeSpace;
			}
			catch (Exception)
			{
			}
			storageLabel.Text = text + ((num >= 0) ? string.Format(L("   ({0:0} GB free)", "   ({0:0} GB livres)", "   ({0:0} Go libres)"), (double)num / 1073741824.0) : "");
		}
	}

	private void MoveStorage()
	{
		if (State() != GameState.None || busyNow || setupBusy)
		{
			MessageBox.Show(this, L("Close the game and wait for the current task first.", "Fecha o jogo e espera pela tarefa atual primeiro.", "Ferme le jeu et attends la fin de la tâche en cours."), "Julia's Launcher");
			return;
		}
		FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
		folderBrowserDialog.Description = L("Pick a folder on the drive for VIctory's files (a new folder named VIctory is made in it).", "Escolhe uma pasta no disco para os ficheiros do VIctory (e criada la uma pasta VIctory).", "Choisis un dossier sur le disque pour les fichiers de VIctory (un dossier VIctory y est créé).");
		folderBrowserDialog.ShowNewFolderButton = true;
		string target;
		using (FolderBrowserDialog folderBrowserDialog2 = folderBrowserDialog)
		{
			if (folderBrowserDialog2.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			target = folderBrowserDialog2.SelectedPath;
		}
		if (!string.Equals(Path.GetFileName(target.TrimEnd('\\')), "VIctory", StringComparison.OrdinalIgnoreCase))
		{
			target = Path.Combine(target, "VIctory");
		}
		if (!target.All((char c) => c < '\u0080'))
		{
			MessageBox.Show(this, L("Pick a folder whose path has no accents or symbols, for example D:\\Games.", "Escolhe uma pasta cujo caminho nao tenha acentos nem simbolos, por exemplo D:\\Games.", "Choisis un dossier dont le chemin n'a ni accents ni symboles, par exemple D:\\Games."), "Julia's Launcher");
			return;
		}
		List<Tuple<string, string>> from = new Tuple<string, string>[3]
		{
			Tuple.Create(cacheDir, Path.Combine(target, "cache")),
			Tuple.Create(dataDir, Path.Combine(target, "data")),
			Tuple.Create(Path.Combine(Path.GetDirectoryName(dataRoot), "engines"), Path.Combine(target, "engines"))
		}.Where((Tuple<string, string> t) => !string.Equals(Path.GetFullPath(t.Item1).TrimEnd('\\'), Path.GetFullPath(t.Item2).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)).ToList();
		long num = 0L;
		foreach (Tuple<string, string> item in from)
		{
			num += FolderBytes(item.Item1);
		}
		long num2 = -1L;
		try
		{
			Directory.CreateDirectory(target);
			num2 = new DriveInfo(Path.GetPathRoot(target)).AvailableFreeSpace;
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "Julia's Launcher");
			return;
		}
		if (num2 >= 0 && num2 < num + 2147483648u)
		{
			MessageBox.Show(this, string.Format(L("That drive has {0:0.0} GB free; VIctory's files take {1:0.0} GB. Free some space or pick another drive.", "Esse disco tem {0:0.0} GB livres; os ficheiros do VIctory ocupam {1:0.0} GB. Liberta espaco ou escolhe outro disco.", "Ce disque a {0:0.0} Go libres ; les fichiers de VIctory font {1:0.0} Go. Libère de la place ou choisis un autre disque."), (double)num2 / 1073741824.0, (double)num / 1073741824.0), "Julia's Launcher");
		}
		else
		{
			if (MessageBox.Show(this, string.Format(L("Move VIctory's files ({0:0.0} GB: shader cache, game data and saves, AMD engine) to {1}?\r\n\r\nYour saves are copied first and removed from the old place only when the copy is complete.", "Mover os ficheiros do VIctory ({0:0.0} GB: cache de shaders, dados e saves do jogo, motor AMD) para {1}?\r\n\r\nOs saves sao copiados primeiro e so saem do sitio antigo quando a copia estiver completa.", "Déplacer les fichiers de VIctory ({0:0.0} Go : cache de shaders, données et sauvegardes du jeu, moteur AMD) vers {1} ?\r\n\r\nLes sauvegardes sont d'abord copiées et ne quittent l'ancien emplacement qu'une fois la copie terminée."), (double)num / 1073741824.0, target), "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
			{
				return;
			}
			Busy(busy: true);
			Thread thread = new Thread(() =>
			{
				bool ok = true;
				List<string> movedAll = new List<string>();
				try
				{
					foreach (Tuple<string, string> item2 in from)
					{
						if (Directory.Exists(item2.Item1))
						{
							Progress(string.Format(L("Moving {0}...", "A mover {0}...", "Déplacement de {0}..."), Path.GetFileName(item2.Item2)), 0.1f + 0.8f * (float)from.IndexOf(item2) / (float)Math.Max(1, from.Count));
							int num3 = RunWait("robocopy", "\"" + item2.Item1.TrimEnd('\\') + "\" \"" + item2.Item2 + "\" /E /MOVE /R:1 /W:1 /NP /NFL /NDL /MT:8", window: false);
							if (num3 >= 8)
							{
								ok = false;
								SafeLog(string.Format(L("Moving {0} stopped (robocopy code {1}); what was not moved is still in {2}.", "A mudanca de {0} parou (codigo {1} do robocopy); o que nao foi movido continua em {2}.", "Le déplacement de {0} s'est arrêté (code robocopy {1}) ; ce qui n'a pas été déplacé est encore dans {2}."), item2.Item1, num3, item2.Item1));
								break;
							}
							movedAll.Add(item2.Item1);
						}
					}
				}
				catch (Exception ex2)
				{
					ok = false;
					SafeLog(ex2.Message);
				}
				try
				{
					BeginInvoke((Action)(() =>
					{
						if (!ok)
						{
							if (movedAll.Contains(cacheDir))
							{
								WriteSetting("cache", Path.Combine(target, "cache"));
								cacheDir = Path.Combine(target, "cache");
							}
							if (movedAll.Contains(dataDir))
							{
								WriteSetting("data", Path.Combine(target, "data"));
								dataDir = Path.Combine(target, "data");
							}
						}
						if (ok)
						{
							WriteSetting("storage", target);
							WriteSetting("cache", Path.Combine(target, "cache"));
							WriteSetting("data", Path.Combine(target, "data"));
							cacheDir = Path.Combine(target, "cache");
							dataDir = Path.Combine(target, "data");
							Log(L("VIctory's files are now in ", "Os ficheiros do VIctory estao agora em ", "Les fichiers de VIctory sont maintenant dans ") + target);
						}
						Progress("", 0f);
						Busy(busy: false);
						ShowStorage();
						Refresh2();
						RefreshShaders();
					}));
				}
				catch (Exception)
				{
					if (ok)
					{
						WriteSetting("storage", target);
						WriteSetting("cache", Path.Combine(target, "cache"));
						WriteSetting("data", Path.Combine(target, "data"));
					}
				}
			});
			thread.IsBackground = true;
			thread.Start();
		}
	}

	private static long FolderBytes(string dir)
	{
		long num = 0L;
		try
		{
			if (!Directory.Exists(dir))
			{
				return 0L;
			}
			foreach (FileInfo item in new DirectoryInfo(dir).EnumerateFiles("*", System.IO.SearchOption.AllDirectories))
			{
				try
				{
					num += item.Length;
				}
				catch (Exception)
				{
				}
			}
		}
		catch (Exception)
		{
		}
		return num;
	}

	private void EnsureAgility()
	{
		Thread thread = new Thread(() =>
		{
			lock (agilityLock)
			{
				try
				{
					string text = null;
					foreach (string ownExeCandidate in OwnExeCandidates)
					{
						if (File.Exists(ownExeCandidate))
						{
							string text2 = Path.Combine(Path.GetDirectoryName(ownExeCandidate), "D3D12", "D3D12Core.dll");
							if (File.Exists(text2) && Sha256(text2) == "70a6d47d335b4a15180544c01a35b84474522a369dc582d85fe057313a3aeaa0")
							{
								text = text ?? text2;
							}
							else
							{
								if (text == null)
								{
									text = FetchAgility();
								}
								if (text == null)
								{
									break;
								}
								Directory.CreateDirectory(Path.GetDirectoryName(text2));
								File.Copy(text, text2, overwrite: true);
								Log(L("D3D12 Agility SDK placed beside victory.exe (needed on Windows 10).", "D3D12 Agility SDK posto ao lado do victory.exe (necessario no Windows 10).", "D3D12 Agility SDK placé à côté de victory.exe (nécessaire sous Windows 10)."));
							}
						}
					}
				}
				catch (Exception ex)
				{
					try
					{
						File.AppendAllText(Program.ErrorFile, string.Concat(DateTime.Now, "  agility: ", ex, Environment.NewLine));
					}
					catch (Exception)
					{
					}
				}
			}
		});
		thread.IsBackground = true;
		thread.Start();
	}

	private string FetchAgility()
	{
		string text = Path.Combine(Path.GetDirectoryName(dataRoot), "agility", "D3D12Core.dll");
		if (File.Exists(text) && Sha256(text) == "70a6d47d335b4a15180544c01a35b84474522a369dc582d85fe057313a3aeaa0")
		{
			return text;
		}
		Directory.CreateDirectory(Path.GetDirectoryName(text));
		string text2 = text + ".part";
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		using (WebClient webClient = new WebClient())
		{
			webClient.Headers[HttpRequestHeader.UserAgent] = "Julias-Launcher";
			webClient.DownloadFile("https://github.com/felicienradio/cold-project-vi/releases/download/v0.3.7/D3D12Core.dll", text2);
		}
		if (Sha256(text2) != "70a6d47d335b4a15180544c01a35b84474522a369dc582d85fe057313a3aeaa0")
		{
			File.Delete(text2);
			return null;
		}
		if (File.Exists(text))
		{
			File.Delete(text);
		}
		File.Move(text2, text);
		return text;
	}

	private static string Fr_(string en)
	{
		if (!FrText.TryGetValue(en, out var value))
		{
			return en;
		}
		return value;
	}
}
