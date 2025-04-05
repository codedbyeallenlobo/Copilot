using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.IO;
using System.Threading.Tasks;

namespace CopilotApp
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CopilotForm());
        }
    }

    public class CopilotForm : Form
    {
        private NotifyIcon trayIcon;
        private Form responseWindow;
        private RichTextBox responseBox;
        private TextBox inputBox;
        private Button askButton;
        private ComboBox modelComboBox; // ComboBox for selecting text model
        private Label modelLabel;       // Label for clarity
        private List<Button> promptButtons;
        private Button addPromptButton;
        private const int HOTKEY_ID = 1;                  // Hotkey for 512x512 region capture
        private const int FULLSCREEN_HOTKEY_ID = 2;         // Hotkey for full-screen capture
        private const int WM_HOTKEY = 0x0312;
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private List<string> prompts = new List<string> { 
            "Summarize this:", 
            "Explain this in simple terms:", 
            "Translate this to Spanish:", 
            "Describe this image:",
            "", "", "", "", ""
        };
        private string selectedPrompt = "Summarize this:";
        private string apiKey;
        private string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prompts.json");

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool AddClipboardFormatListener(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool RemoveClipboardFormatListener(IntPtr hWnd);

        public struct POINT
        {
            public int X;
            public int Y;
        }

        public CopilotForm()
        {
            this.Text = "Copilot App";
            this.Size = new Size(500, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.BackColor = Color.FromArgb(30, 30, 30);

            inputBox = new TextBox
            {
                Location = new Point(10, 10),
                Size = new Size(320, 30),
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10),
                BorderStyle = BorderStyle.FixedSingle
            };

            askButton = new Button
            {
                Text = "Ask",
                Location = new Point(340, 10),
                Size = new Size(100, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            askButton.FlatAppearance.BorderSize = 0;
            askButton.Click += async (s, e) => await ProcessInput();

            promptButtons = new List<Button>();
            int buttonWidth = 130;
            int buttonHeight = 60;
            int startX = 10;
            int startY = 50;
            for (int i = 0; i < 9; i++)
            {
                int row = i / 3;
                int col = i % 3;
                Button promptButton = new Button
                {
                    Text = prompts[i],
                    Location = new Point(startX + col * (buttonWidth + 10), startY + row * (buttonHeight + 10)),
                    Size = new Size(buttonWidth, buttonHeight),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(50, 50, 50),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 10)
                };
                promptButton.FlatAppearance.BorderSize = 0;
                int index = i;
                promptButton.Click += (s, e) =>
                {
                    selectedPrompt = prompts[index];
                    foreach (var btn in promptButtons)
                        btn.BackColor = Color.FromArgb(50, 50, 50);
                    promptButton.BackColor = Color.FromArgb(0, 120, 215);
                };

                ContextMenuStrip promptMenu = new ContextMenuStrip();
                promptMenu.Items.Add("Edit", null, (s, e) => EditPrompt(index, promptButton));
                promptButton.ContextMenuStrip = promptMenu;

                promptButtons.Add(promptButton);
                this.Controls.Add(promptButton);
            }

            addPromptButton = new Button
            {
                Text = "Add Prompt",
                Location = new Point(10, 300),
                Size = new Size(100, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10)
            };
            addPromptButton.FlatAppearance.BorderSize = 0;
            addPromptButton.Click += AddPrompt;

            // Model selection label and ComboBox are placed at the bottom.
            modelLabel = new Label
            {
                Text = "Select Text Model:",
                Location = new Point(10, 350),
                Size = new Size(150, 25),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10)
            };

            modelComboBox = new ComboBox
            {
                Location = new Point(170, 345),
                Size = new Size(140, 30),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10)
            };
            // Add available text-only models.
            modelComboBox.Items.AddRange(new string[] { "gpt-4o-mini", "gpt-4o", "o3-mini" });
            modelComboBox.SelectedItem = "gpt-4o"; // default

            this.Controls.Add(inputBox);
            this.Controls.Add(askButton);
            this.Controls.Add(addPromptButton);
            this.Controls.Add(modelLabel);
            this.Controls.Add(modelComboBox);

            trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "Copilot App"
            };
            trayIcon.ContextMenuStrip = new ContextMenuStrip();
            trayIcon.ContextMenuStrip.Items.Add("Exit", null, (s, e) => ExitApplication());

            LoadApiKey();

            // Register two hotkeys:
            // Ctrl+Shift+S for 512x512 region capture
            RegisterHotKey(this.Handle, HOTKEY_ID, 0x0006, (uint)Keys.S);
            // Ctrl+Shift+F for full screen capture
            RegisterHotKey(this.Handle, FULLSCREEN_HOTKEY_ID, 0x0006, (uint)Keys.F);

            AddClipboardFormatListener(this.Handle);

            // Enhanced response window
            responseWindow = new Form
            {
                Size = new Size(350, 250),
                FormBorderStyle = FormBorderStyle.None,
                BackColor = Color.FromArgb(25, 25, 25),
                TopMost = true,
                StartPosition = FormStartPosition.Manual,
                Padding = new Padding(15),
                Opacity = 0.95
            };
            responseWindow.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(Color.FromArgb(0, 120, 215), 2))
                {
                    e.Graphics.DrawRectangle(pen, 1, 1, responseWindow.Width - 2, responseWindow.Height - 2);
                }
            };

            responseBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.FromArgb(230, 230, 230),
                Font = new Font("Segoe UI", 11, FontStyle.Regular),
                ReadOnly = true,
                Padding = new Padding(10)
            };
            responseWindow.Controls.Add(responseBox);
            responseWindow.FormClosing += (s, e) => e.Cancel = true;

            LoadPrompts();
        }

        private void LoadApiKey()
        {
            string apiConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (File.Exists(apiConfigPath))
            {
                string json = File.ReadAllText(apiConfigPath);
                dynamic config = JsonConvert.DeserializeObject(json);
                apiKey = config?.OpenAI?.ApiKey?.ToString() ?? "default-api-key";
            }
            else
            {
                apiKey = "default-api-key";
            }
        }

        private void LoadPrompts()
        {
            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                var savedPrompts = JsonConvert.DeserializeObject<List<string>>(json);
                if (savedPrompts != null)
                {
                    prompts.Clear();
                    prompts.AddRange(savedPrompts);
                    while (prompts.Count < 9)
                        prompts.Add("");
                    
                    for (int i = 0; i < 9; i++)
                    {
                        promptButtons[i].Text = prompts[i];
                    }
                }
            }
        }

        private void SavePrompts()
        {
            string json = JsonConvert.SerializeObject(prompts);
            File.WriteAllText(configPath, json);
        }

        private void ExitApplication()
        {
            Application.Exit();
        }

        private async Task ProcessInput()
        {
            string input = inputBox.Text;
            if (!string.IsNullOrEmpty(input))
            {
                // For text-only requests, use the model chosen from the ComboBox.
                string selectedModel = modelComboBox.SelectedItem?.ToString() ?? "gpt-4o";
                string response = await SendToChatGPT(input, null, selectedModel);
                ShowResponse(response);
            }
        }

        private void EditPrompt(int index, Button promptButton)
        {
            string editedPrompt = PromptForText("Edit prompt:", prompts[index]);
            if (!string.IsNullOrEmpty(editedPrompt))
            {
                prompts[index] = editedPrompt;
                promptButton.Text = editedPrompt;
                if (selectedPrompt == promptButton.Text)
                    selectedPrompt = editedPrompt;
                SavePrompts();
            }
        }

        private void AddPrompt(object sender, EventArgs e)
        {
            string newPrompt = PromptForText("Enter new prompt:");
            if (!string.IsNullOrEmpty(newPrompt))
            {
                int emptyIndex = prompts.FindIndex(p => string.IsNullOrEmpty(p));
                
                if (emptyIndex >= 0)
                {
                    prompts[emptyIndex] = newPrompt;
                    promptButtons[emptyIndex].Text = newPrompt;
                    
                    Button promptButton = promptButtons[emptyIndex];
                    promptButton.Click -= PromptButton_Click;
                    promptButton.Click += (s, e) =>
                    {
                        selectedPrompt = newPrompt;
                        foreach (var btn in promptButtons)
                            btn.BackColor = Color.FromArgb(50, 50, 50);
                        promptButton.BackColor = Color.FromArgb(0, 120, 215);
                    };
                }
                else
                {
                    MessageBox.Show("All 9 slots are filled. Edit an existing prompt to replace it.");
                    return;
                }
                
                SavePrompts();
            }
        }

        private void PromptButton_Click(object sender, EventArgs e) { }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                if (m.WParam.ToInt32() == HOTKEY_ID)
                {
                    // Capture 512x512 region around the cursor
                    CaptureAndProcessScreenshot();
                }
                else if (m.WParam.ToInt32() == FULLSCREEN_HOTKEY_ID)
                {
                    // Capture full screen
                    CaptureAndProcessFullScreen();
                }
            }
            else if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                ProcessClipboardUpdate();
            }
            base.WndProc(ref m);
        }

        private async void ProcessClipboardUpdate()
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    // Use the selected model for text processing.
                    string selectedModel = modelComboBox.SelectedItem?.ToString() ?? "gpt-4o";
                    string response = await SendToChatGPT(text, null, selectedModel);
                    ShowResponse(response);
                }
            }
        }

        // 512x512 region capture centered around the cursor
        private async void CaptureAndProcessScreenshot()
        {
            GetCursorPos(out POINT point);
            int x = point.X - 256;
            int y = point.Y - 256;

            Bitmap screenshot = new Bitmap(512, 512);
            using (Graphics g = Graphics.FromImage(screenshot))
            {
                g.CopyFromScreen(x, y, 0, 0, new Size(512, 512));
            }

            // For image requests, always use "gpt-4o"
            string response = await SendToChatGPT("Analyze this screenshot:", screenshot, "gpt-4o");
            ShowResponse(response);
            screenshot.Dispose();
        }

        // Full screen capture
        private async void CaptureAndProcessFullScreen()
        {
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            Bitmap screenshot = new Bitmap(bounds.Width, bounds.Height);
            
            using (Graphics g = Graphics.FromImage(screenshot))
            {
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }

            // For image requests, always use "gpt-4o"
            string response = await SendToChatGPT("Analyze this full-screen capture:", screenshot, "gpt-4o");
            ShowResponse(response);
            screenshot.Dispose();
        }

        private string BitmapToBase64(Bitmap bitmap)
        {
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                byte[] imageBytes = ms.ToArray();
                if (imageBytes.Length > 20 * 1024 * 1024)
                {
                    throw new Exception("Image exceeds 20MB limit.");
                }
                return Convert.ToBase64String(imageBytes);
            }
        }

        // Modified SendToChatGPT now accepts a model parameter for text-only requests.
        private async Task<string> SendToChatGPT(string input, Bitmap screenshot = null, string model = "gpt-4o")
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                    var messages = new List<object>();
                    if (screenshot == null)
                    {
                        // Use the model selected from the ComboBox for text-only queries.
                        messages.Add(new { role = "user", content = $"{selectedPrompt} {input}" });
                    }
                    else
                    {
                        string base64Image = BitmapToBase64(screenshot);
                        messages.Add(new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = selectedPrompt + " " + input },
                                new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64Image}", detail = "low" } }
                            }
                        });
                    }

                    var payload = new
                    {
                        model = screenshot == null ? model : "gpt-4o",
                        messages = messages.ToArray(),
                        max_tokens = 1000
                    };

                    string jsonPayload = JsonConvert.SerializeObject(payload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        dynamic data = JsonConvert.DeserializeObject(jsonResponse);
                        return data?.choices[0].message.content ?? "Error: Invalid response format.";
                    }
                    return $"Error: API request failed with status {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private void ShowResponse(string text)
        {
            if (responseWindow.IsDisposed)
            {
                responseWindow = new Form
                {
                    Size = new Size(350, 250),
                    FormBorderStyle = FormBorderStyle.None,
                    BackColor = Color.FromArgb(25, 25, 25),
                    TopMost = true,
                    StartPosition = FormStartPosition.Manual,
                    Padding = new Padding(15),
                    Opacity = 0.95
                };
                responseWindow.Paint += (s, e) =>
                {
                    using (Pen pen = new Pen(Color.FromArgb(0, 120, 215), 2))
                    {
                        e.Graphics.DrawRectangle(pen, 1, 1, responseWindow.Width - 2, responseWindow.Height - 2);
                    }
                };

                responseBox = new RichTextBox
                {
                    Dock = DockStyle.Fill,
                    BorderStyle = BorderStyle.None,
                    BackColor = Color.FromArgb(35, 35, 35),
                    ForeColor = Color.FromArgb(230, 230, 230),
                    Font = new Font("Segoe UI", 11, FontStyle.Regular),
                    ReadOnly = true,
                    Padding = new Padding(10)
                };
                responseWindow.Controls.Add(responseBox);
                responseWindow.FormClosing += (s, e) => e.Cancel = true;
            }

            GetCursorPos(out POINT point);
            responseWindow.Location = new Point(point.X + 10, point.Y + 10);
            
            // Process markdown bold formatting to actual bold text
            responseBox.Clear();
            string[] parts = text.Split(new[] { "**" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 0) // Normal text
                {
                    responseBox.SelectionFont = new Font("Segoe UI", 11, FontStyle.Regular);
                    responseBox.AppendText(parts[i]);
                }
                else // Bold text
                {
                    responseBox.SelectionFont = new Font("Segoe UI", 11, FontStyle.Bold);
                    responseBox.AppendText(parts[i]);
                }
            }
            
            responseWindow.Show();
            responseWindow.Deactivate -= ResponseWindow_Deactivate;
            responseWindow.Deactivate += ResponseWindow_Deactivate;
        }

        private void ResponseWindow_Deactivate(object sender, EventArgs e)
        {
            if (!responseWindow.IsDisposed)
            {
                responseWindow.Hide();
            }
        }

        private string PromptForText(string prompt, string defaultText = "")
        {
            Form inputForm = new Form
            {
                Size = new Size(300, 150),
                Text = "Input",
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Color.FromArgb(30, 30, 30)
            };
            Label label = new Label
            {
                Text = prompt,
                Location = new Point(10, 20),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10)
            };
            TextBox textBox = new TextBox
            {
                Location = new Point(10, 40),
                Width = 260,
                Text = defaultText,
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10),
                BorderStyle = BorderStyle.FixedSingle
            };
            Button okButton = new Button
            {
                Text = "OK",
                Location = new Point(10, 80),
                Size = new Size(80, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10)
            };
            okButton.FlatAppearance.BorderSize = 0;
            okButton.Click += (s, e) => inputForm.Close();
            inputForm.Controls.Add(label);
            inputForm.Controls.Add(textBox);
            inputForm.Controls.Add(okButton);
            inputForm.AcceptButton = okButton;
            inputForm.ShowDialog();
            return textBox.Text;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SavePrompts();
            RemoveClipboardFormatListener(this.Handle);
            UnregisterHotKey(this.Handle, HOTKEY_ID);
            UnregisterHotKey(this.Handle, FULLSCREEN_HOTKEY_ID);
            trayIcon.Visible = false;
            if (!responseWindow.IsDisposed)
            {
                responseWindow.Deactivate -= ResponseWindow_Deactivate;
                responseWindow.Close();
                responseWindow.Dispose();
            }
            base.OnFormClosing(e);
        }
    }
}
