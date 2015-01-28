using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace PMOS
{
    class ConfigObj
    {
        public ConfigObj()
        {
            DIR_SRC = "";
            DIR_DEST = "";
            RENAMING_DIR = "";
            RENAMING_SCRIPT = "";
            DRIVE = "";
            EMAIL_FROM = "";
            PASSWORD = "";
            EMAIL_TO = "";
            TIME_ELAPSED_INTERVAL = -1;
            THRESHOLD_WARN = -1;
            THRESHOLD_CRITICAL = -1;
            ALARM = "00:00";
            DIR_DUPLICATE = "";
            DIR_UNMATCHED = "";
        }

        public string DIR_SRC { get; set; }
        public string DIR_DEST { get; set; }
        public string RENAMING_DIR { get; set; }
        public string RENAMING_SCRIPT { get; set; }
        public string DRIVE { get; set; }
        public string EMAIL_FROM { get; set; }
        public string PASSWORD { get; set; }
        public string EMAIL_TO { get; set; }
        public int TIME_ELAPSED_INTERVAL { get; set; }
        public long THRESHOLD_WARN { get; set; }
        public long THRESHOLD_CRITICAL { get; set; }
        public string ALARM { get; set; }
        public string DIR_DUPLICATE { get; set; }
        public string DIR_UNMATCHED { get; set; }
    }

    class XmlConfigFile
    {
        private string cfg;

        public XmlConfigFile(string configFile)
        {
            cfg = configFile;
        }

        public ConfigObj readConfig()
        {
            ConfigObj obj = new ConfigObj();
            XmlDocument xmlDoc = new XmlDocument();

            xmlDoc.Load(cfg);
            obj.DIR_SRC = xmlDoc.SelectSingleNode("//config/DIR_SRC").InnerText;
            obj.DIR_DEST = xmlDoc.SelectSingleNode("//config/DIR_DEST").InnerText;
            obj.RENAMING_DIR = xmlDoc.SelectSingleNode("//config/RENAMING_DIR").InnerText;
            obj.RENAMING_SCRIPT = xmlDoc.SelectSingleNode("//config/RENAMING_SCRIPT").InnerText;
            obj.DRIVE = xmlDoc.SelectSingleNode("//config/DRIVE").InnerText;
            obj.EMAIL_FROM = xmlDoc.SelectSingleNode("//config/EMAIL_FROM").InnerText;
            obj.PASSWORD = xmlDoc.SelectSingleNode("//config/PASSWORD").InnerText;
            obj.EMAIL_TO = xmlDoc.SelectSingleNode("//config/EMAIL_TO").InnerText;
            obj.TIME_ELAPSED_INTERVAL = int.Parse(xmlDoc.SelectSingleNode("//config/TIME_ELAPSED_INTERVAL").InnerText);
            obj.ALARM = xmlDoc.SelectSingleNode("//config/ALARM").InnerText; ;
            obj.DIR_DUPLICATE = xmlDoc.SelectSingleNode("//config/DIR_DUPLICATE").InnerText;
            obj.DIR_UNMATCHED = xmlDoc.SelectSingleNode("//config/DIR_UNMATCHED").InnerText;
            try
            {
                obj.THRESHOLD_WARN = long.Parse(xmlDoc.SelectSingleNode("//config/THRESHOLD_WARN").InnerText);
            }
            catch (Exception)
            { /* Add Log Message Here */ }
            
            try
            {
                obj.THRESHOLD_CRITICAL = long.Parse(xmlDoc.SelectSingleNode("//config/THRESHOLD_CRITICAL").InnerText);
            }
            catch (Exception)
            { /* Add Log Message Here */ }

            return obj;
        }
    }
}
