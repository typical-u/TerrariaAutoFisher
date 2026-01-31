using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading;
using System.Drawing.Imaging;

namespace TerrariaAutoFisher
{
    public partial class MainForm : Form
    {
        // Win32 API imports
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        // Constants
        private const int HOTKEY_ID = 1;
        private const uint MOD_NONE = 0x0000;
        private const uint VK_F5 = 0x74;
        private const int WM_HOTKEY = 0x0312;
        
        private const byte VK_1 = 0x31;
        private const byte VK_2 = 0x32;
        private const byte VK_3 = 0x33;
        private const byte VK_4 = 0x34;
        private const byte VK_5 = 0x35;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        // Potion timer - 4 minutes
        private const int POTION_CYCLE_INTERVAL = 240; // 240 seconds = 4 minutes

        // State managements
        private bool isEnabled = false;
        private Rectangle scanArea;
        private bool isSelectingArea = false;
        private Point selectionStart;
        private Thread mainThread;
        private Thread miniCamThread;
        private volatile bool shouldStop = false;
        private DateTime cycleStartTime;
        
        // UI Controls
        private Label statusLabel;
        private Label instructionsLabel;
        private Button selectAreaButton;
        private Label areaLabel;
        private CheckBox enableCheckBox;
        private Form overlayForm;
        private PictureBox miniCamBox;
        private Label potionTimerLabel;

        public MainForm()
        {
            InitializeComponent();
            RegisterHotKey(this.Handle, HOTKEY_ID, MOD_NONE, VK_F5);
            this.FormClosing += MainForm_FormClosing;
        }

        private void InitializeComponent()
        {
            this.Text = "Terraria Auto Fisher - Refactored";
            this.Size = new Size(750, 550);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            // Instructions Label
            instructionsLabel = new Label
            {
                Text = "Setup Instructions:\n" +
                       "1. Place Fishing Rod in slot 1\n" +
                       "2. Place Fishing Potion in slot 2 (auto-use every 4 min)\n" +
                       "3. Place Sonar Potion in slot 3 (auto-use every 4 min)\n" +
                       "4. Place Crate Potion in slot 4 (auto-use every 4 min)\n" +
                       "5. Place Chum Bucket in slot 5 (throws 3 after potions)\n" +
                       "6. Select scan area (bobber zone) Bottom of area aligns teeny bit above water level\n" +
                       "7. Press F5 to toggle fishing\n" +
                       "FILL SLOTS 2-5 WITH UNUSABLE ITEMS (like dirt, or acorn) if you do not have the potions",
                Location = new Point(20, 20),
                Size = new Size(450, 160),
                Font = new Font("Arial", 9)
            };
            this.Controls.Add(instructionsLabel);

            // Select Area Button
            selectAreaButton = new Button
            {
                Text = "Select Scan Area",
                Location = new Point(20, 190),
                Size = new Size(200, 40),
                Font = new Font("Arial", 10, FontStyle.Bold)
            };
            selectAreaButton.Click += SelectAreaButton_Click;
            this.Controls.Add(selectAreaButton);

            // Area Label
            areaLabel = new Label
            {
                Text = "Scan Area: Not Selected",
                Location = new Point(20, 240),
                Size = new Size(450, 30),
                Font = new Font("Arial", 9)
            };
            this.Controls.Add(areaLabel);

            // Mini-Cam Box
            miniCamBox = new PictureBox
            {
                Location = new Point(480, 20),
                Size = new Size(240, 180),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            this.Controls.Add(miniCamBox);

            Label miniCamLabel = new Label
            {
                Text = "Live Preview",
                Location = new Point(480, 205),
                Size = new Size(240, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Arial", 9, FontStyle.Bold)
            };
            this.Controls.Add(miniCamLabel);

            // Enable/Disable Checkbox (for click to start/stop)
            enableCheckBox = new CheckBox
            {
                Text = "Enable Auto Fisher (F5)",
                Location = new Point(20, 280),
                Size = new Size(250, 30),
                Font = new Font("Arial", 10, FontStyle.Bold),
                Enabled = false
            };
            enableCheckBox.CheckedChanged += EnableCheckBox_CheckedChanged;
            this.Controls.Add(enableCheckBox);

            // Potion Timer Label
            potionTimerLabel = new Label
            {
                Text = "Next Prep Cycle: Not Active",
                Location = new Point(20, 320),
                Size = new Size(450, 60),
                Font = new Font("Arial", 9),
                ForeColor = Color.DarkGreen
            };
            this.Controls.Add(potionTimerLabel);

            // Status Label
            statusLabel = new Label
            {
                Text = "Status: Idle",
                Location = new Point(20, 390),
                Size = new Size(700, 30),
                Font = new Font("Arial", 10, FontStyle.Bold),
                ForeColor = Color.Blue
            };
            this.Controls.Add(statusLabel);
        }

        private void SelectAreaButton_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;
            Thread.Sleep(500);

            overlayForm = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                WindowState = FormWindowState.Maximized,
                TopMost = true,
                Opacity = 0.3,
                BackColor = Color.Black,
                Cursor = Cursors.Cross
            };

            overlayForm.MouseDown += OverlayForm_MouseDown;
            overlayForm.MouseMove += OverlayForm_MouseMove;
            overlayForm.MouseUp += OverlayForm_MouseUp;
            overlayForm.Paint += OverlayForm_Paint;

            overlayForm.ShowDialog();
        }

        private void OverlayForm_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isSelectingArea = true;
                selectionStart = e.Location;
            }
        }

        private void OverlayForm_MouseMove(object sender, MouseEventArgs e)
        {
            if (isSelectingArea)
            {
                overlayForm.Invalidate();
            }
        }

        private void OverlayForm_MouseUp(object sender, MouseEventArgs e)
        {
            if (isSelectingArea && e.Button == MouseButtons.Left)
            {
                isSelectingArea = false;
                int x = Math.Min(selectionStart.X, e.X);
                int y = Math.Min(selectionStart.Y, e.Y);
                int width = Math.Abs(e.X - selectionStart.X);
                int height = Math.Abs(e.Y - selectionStart.Y);

                scanArea = new Rectangle(x, y, width, height);
                
                overlayForm.Close();
                this.WindowState = FormWindowState.Normal;
                this.Activate();

                areaLabel.Text = $"Scan Area: X={x}, Y={y}, W={width}, H={height}";
                enableCheckBox.Enabled = true;
                UpdateStatus("Scan area selected. Ready to fish!");
            }
        }

        private void OverlayForm_Paint(object sender, PaintEventArgs e)
        {
            if (isSelectingArea)
            {
                Point currentPos = overlayForm.PointToClient(Cursor.Position);
                int x = Math.Min(selectionStart.X, currentPos.X);
                int y = Math.Min(selectionStart.Y, currentPos.Y);
                int width = Math.Abs(currentPos.X - selectionStart.X);
                int height = Math.Abs(currentPos.Y - selectionStart.Y);

                using (Pen pen = new Pen(Color.Red, 2))
                {
                    e.Graphics.DrawRectangle(pen, x, y, width, height);
                }
            }
        }

        private void EnableCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            ToggleFishing();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                if (enableCheckBox.Enabled)
                {
                    enableCheckBox.Checked = !enableCheckBox.Checked;
                }
            }
        }

        private void ToggleFishing()
        {
            isEnabled = !isEnabled;

            if (isEnabled)
            {
                shouldStop = false;
                UpdateStatus("Auto Fisher ENABLED - Starting...");
                statusLabel.ForeColor = Color.Green;
                
                // Start main fishing thread
                mainThread = new Thread(MainFishingLoop);
                mainThread.IsBackground = true;
                mainThread.Start();

                // Start mini-cam update thread
                miniCamThread = new Thread(MiniCamLoop);
                miniCamThread.IsBackground = true;
                miniCamThread.Start();
            }
            else
            {
                shouldStop = true;
                UpdateStatus("Auto Fisher DISABLED - Stopped");
                statusLabel.ForeColor = Color.Red;
                
                // Clear mini-cam
                this.Invoke((MethodInvoker)delegate {
                    miniCamBox.Image = null;
                    potionTimerLabel.Text = "Next Prep Cycle: Not Active";
                });
            }
        }

        private void MainFishingLoop()
        {
            // Start the cycle timer
            cycleStartTime = DateTime.Now;
            
            // Always start with prep phase
            ExecutePrepPhase();
            
            while (!shouldStop)
            {
                try
                {
                    // Check if 4 minutes have passed
                    if ((DateTime.Now - cycleStartTime).TotalSeconds >= POTION_CYCLE_INTERVAL)
                    {
                        UpdateStatus("4 minute timer expired - Pausing autofishing");
                        Thread.Sleep(1000); // Wait 1 second
                        
                        // Reset cycle timer and run prep
                        cycleStartTime = DateTime.Now;
                        ExecutePrepPhase();
                    }
                    
                    // Execute autofishing phase
                    ExecuteAutoFishingCycle();
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error in main loop: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }
        }

        private void ExecutePrepPhase()
        {
            if (!IsTerrariaActive())
            {
                UpdateStatus("Prep paused - Terraria not active");
                return;
            }

            UpdateStatus("PREP PHASE - Starting potion sequence");
            
            // Step 1: Delay 500ms, then go to slot 1
            Thread.Sleep(500);
            PressKey(VK_1);
            
            // Step 2: Delay 300ms, go to slot 2, delay 300ms, drink potion
            Thread.Sleep(300);
            PressKey(VK_2);
            Thread.Sleep(300);
            SimulateClick();
            UpdateStatus("PREP - Fishing potion consumed");
            
            // Step 3: Delay 300ms, go to slot 3, delay 300ms, drink potion
            Thread.Sleep(300);
            PressKey(VK_3);
            Thread.Sleep(300);
            SimulateClick();
            UpdateStatus("PREP - Sonar potion consumed");
            
            // Step 4: Delay 300ms, go to slot 4, delay 300ms, drink potion
            Thread.Sleep(300);
            PressKey(VK_4);
            Thread.Sleep(300);
            SimulateClick();
            UpdateStatus("PREP - Crate potion consumed");
            
            // Step 5: Delay 300ms, go to slot 5, delay 300ms, throw chum (3 times)
            Thread.Sleep(300);
            PressKey(VK_5);
            Thread.Sleep(300);
            
            // Throw chum bucket #1
            SimulateClick();
            Thread.Sleep(300);
            
            // Throw chum bucket #2
            SimulateClick();
            Thread.Sleep(300);
            
            // Throw chum bucket #3
            SimulateClick();
            UpdateStatus("PREP - Chum buckets thrown");
            
            // Delay 500ms, go back to slot 1, delay 1000ms
            Thread.Sleep(500);
            PressKey(VK_1);
            Thread.Sleep(1000);
            
            UpdateStatus("PREP PHASE COMPLETE - Transitioning to autofishing");
            UpdateTimerDisplay();
        }

        private void ExecuteAutoFishingCycle()
        {
            if (!IsTerrariaActive())
            {
                Thread.Sleep(1000);
                return;
            }

            // Step 1: Cast fishing line (left click)
            SimulateClick();
            UpdateStatus("Autofishing - Line cast");
            
            // Step 2: Wait 2000ms before resuming scanning
            Thread.Sleep(2000);
            UpdateStatus("Autofishing - Scanning for bite");
            UpdateTimerDisplay();
            
            // Step 3: Scan until bobber leaves or timeout
            bool bobberGone = WaitForBobberToLeave();
            
            if (bobberGone && !shouldStop)
            {
                // Left click to reel in
                UpdateStatus("Autofishing - Bite detected! Reeling in");
                SimulateClick();
                
                // Wait 500ms, pause scanning, wait 500ms
                Thread.Sleep(500);
                UpdateStatus("Autofishing - Paused after reel");
                Thread.Sleep(500);
                
                // Loop will repeat and cast again
            }
            else if (!shouldStop)
            {
                UpdateStatus("Autofishing - Timeout, recasting");
                Thread.Sleep(500);
            }
        }

        private bool WaitForBobberToLeave()
        {
            DateTime startTime = DateTime.Now;
            Bitmap initialBitmap = null;
            
            try
            {
                // Capture initial state (bobber should be settled)
                initialBitmap = CaptureScreen(scanArea);
                
                while (!shouldStop && (DateTime.Now - startTime).TotalSeconds < 30)
                {
                    // Update timer display periodically
                    if ((DateTime.Now - startTime).TotalSeconds % 5 < 0.2) // Every ~5 seconds
                    {
                        UpdateTimerDisplay();
                    }
                    
                    Thread.Sleep(150);

                    Bitmap currentBitmap = null;
                    try
                    {
                        currentBitmap = CaptureScreen(scanArea);

                        // Check if bobber has left the frame (significant change)
                        if (BobberHasLeft(initialBitmap, currentBitmap))
                        {
                            return true;
                        }
                    }
                    finally
                    {
                        currentBitmap?.Dispose();
                    }
                }
                
                return false;
            }
            finally
            {
                initialBitmap?.Dispose();
            }
        }

        private bool BobberHasLeft(Bitmap initial, Bitmap current)
        {
            if (initial == null || current == null) return false;

            int changeCount = 0;
            int threshold = 40; // Color difference threshold
            int requiredChanges = (initial.Width * initial.Height) / 20; // 5% of pixels must change

            BitmapData data1 = initial.LockBits(new Rectangle(0, 0, initial.Width, initial.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            BitmapData data2 = current.LockBits(new Rectangle(0, 0, current.Width, current.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            try
            {
                unsafe
                {
                    byte* ptr1 = (byte*)data1.Scan0;
                    byte* ptr2 = (byte*)data2.Scan0;
                    int bytes = Math.Abs(data1.Stride) * initial.Height;

                    for (int i = 0; i < bytes; i += 3)
                    {
                        int diff = Math.Abs(ptr1[i] - ptr2[i]) +
                                  Math.Abs(ptr1[i + 1] - ptr2[i + 1]) +
                                  Math.Abs(ptr1[i + 2] - ptr2[i + 2]);

                        if (diff > threshold)
                        {
                            changeCount++;
                            if (changeCount > requiredChanges)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            finally
            {
                initial.UnlockBits(data1);
                current.UnlockBits(data2);
            }

            return false;
        }

        private Bitmap CaptureScreen(Rectangle area)
        {
            Bitmap bitmap = new Bitmap(area.Width, area.Height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(area.Location, Point.Empty, area.Size);
            }
            return bitmap;
        }

        private void SimulateClick()
        {
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // Left button down
            Thread.Sleep(50);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // Left button up
        }

        private void PressKey(byte keyCode)
        {
            keybd_event(keyCode, 0, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            keybd_event(keyCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private bool IsTerrariaActive()
        {
            IntPtr handle = GetForegroundWindow();
            System.Text.StringBuilder title = new System.Text.StringBuilder(256);
            GetWindowText(handle, title, 256);
            return title.ToString().Contains("Terraria");
        }

        private void UpdateTimerDisplay()
        {
            if (!isEnabled) return;
            
            try
            {
                DateTime now = DateTime.Now;
                int timeElapsed = (int)(now - cycleStartTime).TotalSeconds;
                int timeRemaining = POTION_CYCLE_INTERVAL - timeElapsed;

                this.Invoke((MethodInvoker)delegate {
                    potionTimerLabel.Text = $"Next Prep Cycle: {FormatTime(timeRemaining)}";
                });
            }
            catch
            {
                // Ignore invoke errors
            }
        }

        private void MiniCamLoop()
        {
            while (!shouldStop)
            {
                try
                {
                    Thread.Sleep(200); // Update 5 times per second

                    if (scanArea.Width > 0 && scanArea.Height > 0)
                    {
                        Bitmap screenshot = CaptureScreen(scanArea);
                        
                        this.Invoke((MethodInvoker)delegate {
                            if (miniCamBox.Image != null)
                            {
                                miniCamBox.Image.Dispose();
                            }
                            miniCamBox.Image = (Bitmap)screenshot.Clone();
                        });

                        screenshot?.Dispose();
                    }
                }
                catch
                {
                    // Ignore errors in mini-cam thread
                }
            }
        }

        private string FormatTime(int seconds)
        {
            if (seconds < 0) return "Ready!";
            int mins = seconds / 60;
            int secs = seconds % 60;
            return $"{mins}:{secs:D2}";
        }

        private void UpdateStatus(string message)
        {
            try
            {
                if (statusLabel.InvokeRequired)
                {
                    statusLabel.Invoke((MethodInvoker)delegate {
                        statusLabel.Text = $"Status: {message}";
                    });
                }
                else
                {
                    statusLabel.Text = $"Status: {message}";
                }
            }
            catch
            {
                // Ignore invoke errors when form is closing
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            shouldStop = true;
            UnregisterHotKey(this.Handle, HOTKEY_ID);
            
            // Give threads time to stop
            Thread.Sleep(500);
        }
    }
}
