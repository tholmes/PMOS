using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Net;
using System.Net.Mail;
using System.ServiceProcess;
using System.Text;
using System.Timers;

namespace PMOS
{
    public partial class ScheduledService : ServiceBase
    {
        // Constants
        private const long BYTES_IN_GIGABYTE = 1073741824; // 1 Gigabyte
        private const int ALERTS_TIME_ELAPSED_INTERVAL = 86400000; // 24 hours
        private const int INITIAL_ALERT_INTERVAL = 10000; // 10 seconds
        private const string CONFIG_FILE_LOCATION = "C:\\V0.17\\RenamingServiceCfg.xml";
        private const string GET_FILE_INFO_SCRIPT = "C:\\V0.17\\GetFileInfo.ps1";
        private const string LOG_FILE = "C:\\V0.17\\ScheduledServiceLog.txt";

        private string DIR_SRC;
        private string DIR_DEST;

        private string ALARM;
        private string DIR_DUPLICATE;
        private string DIR_UNMATCHED;

        private string RENAMING_DIR;
        private string RENAMING_SCRIPT;
        private string DRIVE;

        string EMAIL_FROM;
        string PASSWORD;
        string EMAIL_TO;
 
        private int RECORDINGS_TIME_ELAPSED_INTERVAL;

        private long THRESHOLD_WARN;
        private long THRESHOLD_CRITICAL;

        private Server server;
        private System.Threading.Thread serverThread;
        private bool isIntervalOnSchedule;
        private Timer timerRecordings;
        private Timer timerAlerts;
        private FileSystemWatcher recordingsWatcher;
        private List<string> showsList;

        // Constructor
        public ScheduledService()
        {
            InitializeComponent();

            // Initialize server
            server = new Server();

            // Initialize isIntervalOnSchedule (presume it's not)
            isIntervalOnSchedule = false;

            // Initialize config variables
            readConfigFile();

            // Initialize the timer for recordings
            timerRecordings = new Timer();

            // Initialize the timer for directory alerts
            timerAlerts = new Timer();

            // Create FileSystemWatcher with path
            recordingsWatcher = new FileSystemWatcher(DIR_SRC);

            // Create Shows List
            showsList = new List<string>();
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                serverThread = new System.Threading.Thread(new System.Threading.ThreadStart(server.create));
                serverThread.Start();
                while (!serverThread.IsAlive) ; // wait for server to get all booted up
                LogEvent("server thread started");
            }
            catch (Exception ex)
            {
                LogEvent("server thread NOT started: " + ex.Message);
            }

            startRecordingsDirWatcher();

            startRecordingsTimer();

            startAlertsTimer();

            SendMail("STARTED!", "Media Renaming Service Has STARTED.");

            string msg = string.Format("SERVICE STARTED:");
            LogEvent(msg);

            // Do any preliminary work here...
            processShowsAlreadyInWatchedDir();
        }

        private void processShowsAlreadyInWatchedDir()
        {
            string[] showsInWatchedDir = Directory.GetFiles(DIR_SRC, "*.wtv");
            foreach (string show in showsInWatchedDir)
            {
                processNewShow(Path.GetFileName(show));
            }
            
            // manually call the timer-callback, and start moving recorded shows
            OnRecordingsElapsedTime(null, null);
        }

        private void startRecordingsDirWatcher()
        {
            //Watch for changes in creation of files.
            recordingsWatcher.NotifyFilter = NotifyFilters.FileName;

            // Add event handlers.
            recordingsWatcher.Created += new FileSystemEventHandler(OnRecordingCreated);

            // Begin watching.
            recordingsWatcher.EnableRaisingEvents = true;
        }

        private void startRecordingsTimer()
        {
            //make sure timer starts off disabled
            timerRecordings.Enabled = false;

            //handle Elapsed event
            timerRecordings.Elapsed += new ElapsedEventHandler(OnRecordingsElapsedTime);

            //This statement is used to set time elapsed interval. (1 second = 1,000 milliseconds)
            timerRecordings.Interval = RECORDINGS_TIME_ELAPSED_INTERVAL;
        }

        private void startAlertsTimer()
        {
            //make sure timer starts off enabled
            timerAlerts.Enabled = true;

            //handle Elapsed event
            timerAlerts.Elapsed += new ElapsedEventHandler(OnAlertsElapsedTime);

            //This statement is used to set time elapsed interval. (1 second = 1,000 milliseconds)
            timerAlerts.Interval = INITIAL_ALERT_INTERVAL; // 10 seconds
        }

        private void readConfigFile()
        {
            XmlConfigFile xml = new XmlConfigFile(CONFIG_FILE_LOCATION);
            ConfigObj configFile = xml.readConfig();

            DIR_SRC = configFile.DIR_SRC;
            DIR_DEST = configFile.DIR_DEST;
            RENAMING_DIR = configFile.RENAMING_DIR;
            RENAMING_SCRIPT = configFile.RENAMING_SCRIPT;
            DRIVE = configFile.DRIVE;
            EMAIL_FROM = configFile.EMAIL_FROM;
            PASSWORD = configFile.PASSWORD;
            EMAIL_TO = configFile.EMAIL_TO;
            RECORDINGS_TIME_ELAPSED_INTERVAL = configFile.TIME_ELAPSED_INTERVAL;
            THRESHOLD_WARN = configFile.THRESHOLD_WARN;
            THRESHOLD_CRITICAL = configFile.THRESHOLD_CRITICAL;
            ALARM = configFile.ALARM;
            DIR_DUPLICATE = configFile.DIR_DUPLICATE;
            DIR_UNMATCHED = configFile.DIR_UNMATCHED;
        }

        protected override void OnStop()
        {
            serverThread.Abort();
            serverThread.Join();

            recordingsWatcher.EnableRaisingEvents = false;
            recordingsWatcher.Dispose();

            timerRecordings.Enabled = false;
            timerRecordings.Dispose();

            timerAlerts.Enabled = false;
            timerAlerts.Dispose();

            SendMail("STOPPED!", "Media Renaming Service Has Stopped.");

            string msg = string.Format("SERVICE STOPPED");
            LogEvent(msg);
        }

        void OnRecordingCreated(object sender, FileSystemEventArgs e)
        {
            processNewShow(e.Name);
        }

        private void processNewShow(string name)
        {
            string msg = string.Format("New Show Created:\n\t{0}", name);
            LogEvent(msg);

            addShowToQueue(name);

            checkDiskSpace();
        }

        private void OnRecordingsElapsedTime(object source, ElapsedEventArgs e)
        {
            List<string> removedList = new List<string>();

            foreach (string show in showsList)
            {
                if (moveShow(show))
                    removedList.Add(show);
            }

            foreach (string show in removedList)
            {
                showsList.Remove(show);
            }

            if (showsList.Count == 0)
            {
                timerRecordings.Enabled = false;

                // Launch PowerShell script
                try
                {
                    PowerShell PowerShellInstance = PowerShell.Create();

                    string msg = string.Format("Queue is empty:\n\tTimer disabled\n\tLaunching Renaming-script...");
                    LogEvent(msg);

                    string scriptText = System.IO.File.ReadAllText(RENAMING_SCRIPT);
                    PowerShellInstance.AddScript(scriptText);

                    Collection<PSObject> PSOutput = PowerShellInstance.Invoke();

                    LogEvent("Renaming script, FINISHED:");

                    if (removedList.Count > 0) // Do not send email, if there were no shows in this list.
                        SendMail("New Shows Added To Library", "Check 'Recently Added' to view your newly added shows!");

                    // Delete shows in Duplicate folder
                    deleteDuplicates();

                    /*
                    LogEvent("Renaming script, BEGIN:");

                    while (result.IsCompleted == false)
                    {
                        LogEvent("Renaming script, BUSY:");
                        System.Threading.Thread.Sleep(5000);
                    }
                    */
                }
                catch (Exception ex)
                {
                    string msg = string.Format("Something bad happened:\n\t{0}", ex.Message);
                    LogEvent(msg);
                }
            }
        }

        private void deleteDuplicates()
        {
            string[] duplicateFileEntries = Directory.GetFiles(DIR_DUPLICATE, "*.wtv");
            foreach (string filePath in duplicateFileEntries)
            {
                try
                {
                    System.IO.File.Delete(filePath);
                    string msg = string.Format("DeleteDuplicates:\n\t{0}", filePath);
                    LogEvent(msg);
                }
                catch (Exception ex)
                {
                    string msg = string.Format("DeleteDuplicates Exception:\n\t{0}", ex.Message);
                    LogEvent(msg);
                }
            }
        }

        private void OnAlertsElapsedTime(object source, ElapsedEventArgs e)
        {
            if (ALARM.Length == 0) // Check to see if anything was read or not, from the config file
            {
                timerAlerts.Enabled = false;
                return;
            }

            string msg = configureInterval();
            LogEvent("OnAlertsElapsedTime:" + msg);

            if(isIntervalOnSchedule)
                checkDuplicateAndUnmatchedDirs();
        }

        private string configureInterval()
        {
            DateTime alarmParsed = DateTime.Parse(ALARM);
            DateTime now = DateTime.Now;
            double timeSpan = (alarmParsed - now).TotalMilliseconds;
            string msg = "";
            msg += ("\n\tAlarm = " + alarmParsed);
            msg += ("\n\tNow = " + now);
            msg += ("\n\tTimeSpan = " + timeSpan);

            //if(abs(alarm - now) <= 1 sec){ set reg. interval }else{ determine when to set interval }
            if (Math.Abs(timeSpan) <= 1000)
            {
                timerAlerts.Interval = ALERTS_TIME_ELAPSED_INTERVAL;
                msg += ("\n\tsetting interval to " + ALERTS_TIME_ELAPSED_INTERVAL);
                isIntervalOnSchedule = true;
            }
            // otherwise determine length of time between now and specified time
            else
            {
                if (timeSpan > 0) // use timeSpan as interval
                {
                    timerAlerts.Interval = timeSpan;
                }
                else // regInterval + timeSpan
                {
                    timerAlerts.Interval = ALERTS_TIME_ELAPSED_INTERVAL + timeSpan;
                }
                msg += ("\n\tsetting interval to " + timerAlerts.Interval);
            }

            return msg;
        }

        private void addShowToQueue(string show)
        {
            showsList.Add(show);

            if (!timerRecordings.Enabled)
            {
                //enabling the timer
                timerRecordings.Enabled = true;

                string msgTimer = string.Format("Timer enabled:"); 
                LogEvent(msgTimer);
            }

            string msg = string.Format("Added Show to queue:\n\t{0}", show);
            LogEvent(msg);

            SendMail("Recording In Progress", show);
        }

        private void checkDuplicateAndUnmatchedDirs()
        {
            string[] duplicateFileEntries = Directory.GetFiles(DIR_DUPLICATE, "*.wtv");
            string[] unmatchedFileEntries = Directory.GetFiles(DIR_UNMATCHED, "*.wtv");
            
            string duplicateMessage = "";
            string unmatchedMessage = "";

            if (duplicateFileEntries.Length > 0)
            {
                duplicateMessage += "DUPLICATE FILES:";
                foreach (string file in duplicateFileEntries)
                {
                    duplicateMessage += ("\n   " + Path.GetFileName(file));
                }
            }
            if (unmatchedFileEntries.Length > 0)
            {
                if (duplicateFileEntries.Length > 0)
                    unmatchedMessage += "\n\n\n";
                unmatchedMessage += "UNMATCHED FILES:";
                foreach (string file in unmatchedFileEntries)
                {
                    unmatchedMessage += ("\n   " + Path.GetFileName(file));
                }
            }

            string logMsg = duplicateMessage + unmatchedMessage;
            if (duplicateFileEntries.Length == 0 && unmatchedFileEntries.Length == 0)
            {
                logMsg = "There were no outstanding files in Duplicate Directory or Unmatched Directory, to report";
            }
            else
            {
                SendMail("Duplicate/Unmatched Files", duplicateMessage + unmatchedMessage);
            }
            LogEvent(logMsg);
        }

        private string[] getFileInfo(string recordingDir, string recording)
        {
            string title = "";
            string subtitle = "";
            string []info = new string[2];

            // Launch PowerShell script
            try
            {
                PowerShell PowerShellInstance = PowerShell.Create();
                
                string scriptText = System.IO.File.ReadAllText(GET_FILE_INFO_SCRIPT);
                PowerShellInstance.AddScript(scriptText);
                PowerShellInstance.AddParameter("recordingDir", recordingDir);
                PowerShellInstance.AddParameter("recording", recording);

                Collection<PSObject> PSOutput = PowerShellInstance.Invoke();

                string broadcast_date = PSOutput[0].ToString();

                if (!(broadcast_date.CompareTo("") == 0))
                {
                    title = PSOutput[1].ToString();
                    subtitle = PSOutput[2].ToString();
                    info[0] = "Show";
                    info[1] = string.Format("{0} - {1}", title, subtitle);
                }
                else
                {
                    info[0] = "Movie";
                    info[1] = PSOutput[1].ToString();
                }

            }
            catch (Exception)
            {
                //Console.WriteLine("EXCEPTION: " + ex.Message);
            }

            return info;
        }

        bool moveShow(string fileName)
        {
            string srcFile = System.IO.Path.Combine(DIR_SRC, fileName);
            string destFile = System.IO.Path.Combine(DIR_DEST, fileName);

            if (!System.IO.Directory.Exists(DIR_DEST))
            {
                System.IO.Directory.CreateDirectory(DIR_DEST);
            }

            try
            {
                if (System.IO.File.Exists(srcFile))
                {
                    System.IO.File.Move(srcFile, destFile);
                    //showsList.Remove(fileName);

                    string[] info = getFileInfo(DIR_DEST, fileName);
                    SendMail("New " + info[0] + " Recorded!", info[1]);

                    string msg = string.Format("Moved {0}:\n\t{1}", info[0], fileName);
                    LogEvent(msg);
                }
                else
                {
                    string msg = string.Format("File does not exist, removing from queue:\n\t{0}", fileName);
                    LogEvent(msg);
                }

                return true;
            }
            catch (Exception ex)
            {
                string msg = string.Format("Tried moving Recording:\n\t{0}\n\t{1}", fileName, ex.Message);
                LogEvent(msg);

                return false;
            }
        }

        //http://stackoverflow.com/questions/1412395/how-can-i-check-for-available-disk-space
        public void checkDiskSpace()
        {
            if (DRIVE.Length == 0 || THRESHOLD_WARN == -1 || THRESHOLD_CRITICAL == -1)
                return;

            try
            {
                DriveInfo driveInfo = new DriveInfo(DRIVE);
                long freeSpaceBytes = driveInfo.AvailableFreeSpace;
                long freeSpaceGigs = freeSpaceBytes / BYTES_IN_GIGABYTE;

                string msg = string.Format("CheckDiskSpace:\n\t{0} GB", freeSpaceGigs);
                LogEvent(msg);

                // Set email level to Warning
                if (freeSpaceGigs < THRESHOLD_WARN)
                {
                    string level = "WARNING";
                    
                    // set email level to Critical
                    if (freeSpaceGigs < THRESHOLD_CRITICAL)
                    {
                        level = "CRITICAL";
                    }

                    // Send Email
                    string subject = string.Format("Low Disk Space: {0}", level);
                    string body = string.Format("{0} GB remaining!", freeSpaceGigs);
                    SendMail(subject, body);
                }
            }
            catch (System.IO.IOException)
            {
                string msg = string.Format("CheckDiskSpace:\n\tError - Could not check disk space");
                LogEvent(msg);
            }
        }

        //http://stackoverflow.com/questions/9201239/send-e-mail-via-smtp-using-c-sharp
        public void SendMail(string subject, string message)
        {
            if (EMAIL_TO.Length == 0)
                return;

            string status = "SUCCESS";

            try
            {
                SmtpClient client = new SmtpClient();
                client.Port = 587;
                client.Host = "smtp.gmail.com";
                client.EnableSsl = true;
                client.Timeout = 10000;
                client.DeliveryMethod = SmtpDeliveryMethod.Network;
                client.UseDefaultCredentials = false;
                client.Credentials = new System.Net.NetworkCredential(EMAIL_FROM, PASSWORD);

                string timeStamp = DateTime.Now.ToString();
                subject += " ";
                subject += timeStamp;

                MailMessage mm = new MailMessage(EMAIL_FROM, EMAIL_TO, subject, message);
                mm.BodyEncoding = UTF8Encoding.UTF8;
                mm.DeliveryNotificationOptions = DeliveryNotificationOptions.OnFailure;

                client.Send(mm);
            }
            catch (Exception)
            { status = "FAILED"; }

            string msg = string.Format("SendEmail: \n\tFrom - {0}\n\tTo - {1}\n\tSubject - {2}\n\tMessage - {3}\n\t{4}!", EMAIL_FROM, EMAIL_TO, subject, message, status);
            LogEvent(msg);
        }

        private void LogEvent(string content)
        {
            if (LOG_FILE.Length == 0)
                return;

            //set up a filestream
            FileStream fs = new FileStream(LOG_FILE, FileMode.OpenOrCreate, FileAccess.Write);

            //set up a streamwriter for adding text
            StreamWriter sw = new StreamWriter(fs);

            //find the end of the underlying filestream
            sw.BaseStream.Seek(0, SeekOrigin.End);

            //add the text
            string msg = string.Format("{0}\n\t{1}\n\n", content, DateTime.Now);
            sw.WriteLine(msg);

            //add the text to the underlying filestream
            sw.Flush();

            //close the writer
            sw.Close();
        }
    }
}
