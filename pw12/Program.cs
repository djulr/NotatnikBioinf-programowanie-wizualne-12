namespace pw12
{
    using System;
    using System.Collections.Generic;
    using System.Data.SQLite;
    using System.IO;
    using System.Linq;
    using System.Windows.Forms;
    using QuestPDF.Fluent;
    using QuestPDF.Helpers;
    using QuestPDF.Infrastructure;
    using DrawingSize = System.Drawing.Size;
    using System.Drawing;

    class Program
    {
        static SQLiteConnection connection;

        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            InitializeDatabase();
            Application.Run(new MainForm());
        }

        static void InitializeDatabase()
        {
            if (!File.Exists("notebook.db"))
            {
                SQLiteConnection.CreateFile("notebook.db");
            }

            connection = new SQLiteConnection("Data Source=notebook.db;Version=3;");
            connection.Open();

            string createSessionTable = @"CREATE TABLE IF NOT EXISTS Sessions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Title TEXT NOT NULL,
            CreatedAt TEXT NOT NULL
        );";

            string createEntriesTable = @"CREATE TABLE IF NOT EXISTS Entries (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SessionId INTEGER,
            Description TEXT,
            AttachmentPath TEXT,
            FOREIGN KEY(SessionId) REFERENCES Sessions(Id)
        );";

            using var cmd1 = new SQLiteCommand(createSessionTable, connection);
            cmd1.ExecuteNonQuery();
            using var cmd2 = new SQLiteCommand(createEntriesTable, connection);
            cmd2.ExecuteNonQuery();
        }

        public class MainForm : Form
        {
            ListBox sessionList = new ListBox();
            Button newSessionBtn = new Button() { Text = "Nowa sesja" };
            Button addEntryBtn = new Button() { Text = "Dodaj wpis" };
            Button exportPdfBtn = new Button() { Text = "Eksportuj PDF" };
            ListBox entryList = new ListBox();
            int? selectedSessionId = null;

            public MainForm()
            {
                Text = "BioInfo Notebook";
                this.Size = new DrawingSize(800, 600);

                sessionList.Location = new Point(10, 10);
                sessionList.Size = new DrawingSize(250, 400);
                sessionList.SelectedIndexChanged += (s, e) =>
                {
                    if (sessionList.SelectedItem is Session sObj)
                    {
                        selectedSessionId = sObj.Id;
                        LoadEntries();
                    }
                };

                entryList.Location = new Point(270, 10);
                entryList.Size = new DrawingSize(500, 400);

                newSessionBtn.Location = new Point(10, 420);
                newSessionBtn.Size = new DrawingSize(120, 40);
                newSessionBtn.Click += (s, e) => CreateSession();

                addEntryBtn.Location = new Point(130, 420);
                addEntryBtn.Size = new DrawingSize(120, 40);
                addEntryBtn.Click += (s, e) => AddEntry();

                exportPdfBtn.Location = new Point(250, 420);
                exportPdfBtn.Size = new DrawingSize(120, 40);
                exportPdfBtn.Click += (s, e) => ExportToPdf();

                Controls.Add(sessionList);
                Controls.Add(entryList);
                Controls.Add(newSessionBtn);
                Controls.Add(addEntryBtn);
                Controls.Add(exportPdfBtn);

                LoadSessions();
            }

            void LoadSessions()
            {
                sessionList.Items.Clear();
                var cmd = new SQLiteCommand("SELECT * FROM Sessions", connection);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    sessionList.Items.Add(new Session
                    {
                        Id = Convert.ToInt32(reader["Id"]),
                        Title = reader["Title"].ToString(),
                        CreatedAt = reader["CreatedAt"].ToString()
                    });
                }
            }

            void LoadEntries()
            {
                entryList.Items.Clear();
                if (selectedSessionId == null) return;

                var cmd = new SQLiteCommand("SELECT * FROM Entries WHERE SessionId = @sid", connection);
                cmd.Parameters.AddWithValue("@sid", selectedSessionId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    entryList.Items.Add($"{reader["Description"]} | Za³¹cznik: {Path.GetFileName(reader["AttachmentPath"].ToString())}");
                }
            }

            void CreateSession()
            {
                string title = Microsoft.VisualBasic.Interaction.InputBox("Tytu³ sesji:", "Nowa sesja", "Sesja");
                if (string.IsNullOrWhiteSpace(title)) return;

                var cmd = new SQLiteCommand("INSERT INTO Sessions (Title, CreatedAt) VALUES (@title, @date)", connection);
                cmd.Parameters.AddWithValue("@title", title);
                cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                cmd.ExecuteNonQuery();

                LoadSessions();
            }

            void AddEntry()
            {
                if (selectedSessionId == null)
                {
                    MessageBox.Show("Wybierz sesjê!");
                    return;
                }

                string desc = Microsoft.VisualBasic.Interaction.InputBox("Opis wpisu:", "Nowy wpis", "Opis analizy...");
                if (string.IsNullOrWhiteSpace(desc)) return;

                OpenFileDialog ofd = new OpenFileDialog();
                if (ofd.ShowDialog() != DialogResult.OK) return;

                string path = ofd.FileName;
                var cmd = new SQLiteCommand("INSERT INTO Entries (SessionId, Description, AttachmentPath) VALUES (@sid, @desc, @path)", connection);
                cmd.Parameters.AddWithValue("@sid", selectedSessionId);
                cmd.Parameters.AddWithValue("@desc", desc);
                cmd.Parameters.AddWithValue("@path", path);
                cmd.ExecuteNonQuery();

                LoadEntries();
            }



            void ExportToPdf()
            {
                if (selectedSessionId == null) return;

                var sessionCmd = new SQLiteCommand("SELECT * FROM Sessions WHERE Id = @id", connection);
                sessionCmd.Parameters.AddWithValue("@id", selectedSessionId);
                using var sReader = sessionCmd.ExecuteReader();
                sReader.Read();
                var title = sReader["Title"].ToString();
                var createdAt = sReader["CreatedAt"].ToString();

                var entryCmd = new SQLiteCommand("SELECT * FROM Entries WHERE SessionId = @sid", connection);
                entryCmd.Parameters.AddWithValue("@sid", selectedSessionId);
                var entries = new List<(string desc, string path)>();
                using var eReader = entryCmd.ExecuteReader();
                while (eReader.Read())
                {
                    entries.Add((eReader["Description"].ToString(), eReader["AttachmentPath"].ToString()));
                }

                var fileName = $"{title.Replace(" ", "_")}_{DateTime.Now:yyyyMMddHHmm}.pdf";

                try
                {
                    QuestPDF.Settings.License = LicenseType.Community;
                    Document.Create(container =>
                    {
                        container.Page(page =>
                        {
                            page.Margin(50);
                            page.Header().Text($"{title} ({createdAt})").FontSize(20).Bold();

                            page.Content().Column(col =>
                            {
                                foreach (var (desc, path) in entries)
                                {
                                    col.Item().Text(desc).FontSize(12);

                                    if (File.Exists(path))
                                    {
                                        string ext = Path.GetExtension(path).ToLower();

                                        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                                        {
                                            try
                                            {
                                                byte[] imgBytes = File.ReadAllBytes(path);
                                                col.Item().Image(imgBytes, ImageScaling.FitWidth);
                                            }
                                            catch
                                            {
                                                col.Item().Text("[B³¹d podczas wczytywania obrazu]").Italic();
                                            }
                                        }
                                        else if (ext == ".fasta" || ext == ".fa" || ext == ".csv" || ext == ".txt")
                                        {
                                            try
                                            {
                                                string content = File.ReadAllText(path);
                                                string preview = content.Length > 1000 ? content.Substring(0, 1000) + "..." : content;

                                                
                                                List<string> lines = new();
                                                for (int i = 0; i < preview.Length; i += 80)
                                                    lines.Add(preview.Substring(i, Math.Min(80, preview.Length - i)));

                                                col.Item().Text($"Zawartoœæ pliku {Path.GetFileName(path)}:").Bold();

                                                foreach (var line in lines)
                                                    col.Item().Text(line).FontSize(9).FontFamily("Courier New");
                                            }
                                            catch
                                            {
                                                col.Item().Text("[Nie uda³o siê odczytaæ pliku tekstowego]").Italic();
                                            }
                                        }
                                        else
                                        {
                                            col.Item().Text($"Za³¹cznik: {Path.GetFileName(path)} (typ nieobs³ugiwany)").Italic();
                                        }
                                    }
                                }
                            });

                            page.Footer().AlignCenter().Text($"Wygenerowano: {DateTime.Now}");
                        });
                    }).GeneratePdf(fileName);

                    MessageBox.Show($"Zapisano PDF jako: {fileName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"B³¹d podczas generowania PDF:\n{ex.Message}", "B³¹d", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        class Session
        {
            public int Id;
            public string Title;
            public string CreatedAt;
            public override string ToString() => $"{Title} ({CreatedAt})";
        }
    }
}