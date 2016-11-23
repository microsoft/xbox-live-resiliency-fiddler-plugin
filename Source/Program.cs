using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Windows.Forms;
using Fiddler;

[assembly: Fiddler.RequiredVersion("2.4.0.0")]

namespace XboxLiveResiliencyPluginForFiddler
{
    public class XboxLiveResiliencyPluginForFiddler : IAutoTamper, IHandleExecAction
    {
        /// <summary>
        /// Defines the different kinds of failures
        /// </summary>
        public enum FailureType
        {
            NotFound,           // 404
            ServiceUnavailable, // 503
            RateLimitBurst,     // 429 (Burst)
            RateLimitSustained  // 429 (Sustained)
        }

        /// <summary>
        /// Indicates whether the plugin is enabled
        /// </summary>
        private bool pluginEnabled = false;

        /// <summary>
        /// Indicates whether to block external services
        /// </summary>
        private bool blockExternalServices = false;

        /// <summary>
        /// Current failure type to inject in the response
        /// </summary>
        private FailureType failureType;

        /// <summary>
        /// Contains the list of blocked hosts
        /// </summary>
        private HostList blockedHostList = new HostList();

        /// <summary>
        /// Right-click menu item, which will allow user to block the selected host domain
        /// </summary>
        private MenuItem blockThisServiceMenuItem = new MenuItem("Block this Service");

        /// <summary>
        /// Root menu item that exists in the main Fiddler UI
        /// </summary>
        private MenuItem rootMenuItem = new MenuItem("Xbox Live Resiliency");

        /// <summary>
        /// Button that toggles whether the plugin functionality is turned on
        /// </summary>
        private MenuItem enablePluginMenuItem = new MenuItem("Enabled");

        /// <summary>
        /// List of failure type menu items
        /// </summary>
        private List<MenuItem> failureTypeMenuItems = new List<MenuItem>();

        /// <summary>
        /// List of service menu items
        /// </summary>
        private List<MenuItem> serviceMenuItems = new List<MenuItem>();

        /// <summary>
        /// Collection of failure types (key = name, value = enumeration type)
        /// </summary>
        private Dictionary<string, FailureType> failures = new Dictionary<string, FailureType>();

        /// <summary>
        /// Collection of services to block (key = name, value = endpoint)
        /// </summary>
        private Dictionary<string, string> services = new Dictionary<string, string>();

        /// <summary>
        /// Indicates whether an update check has been performed
        /// </summary>
        private static bool updateCheck = false;

        /// <summary>
        /// Used internally to denote external service host. Workaround for Fiddler's host list datatype requirements. 
        /// </summary>
        private static string ExternalServicesGuid = "98035eb0-74c9-4833-8c35-9de06241cb66.guid";

        /// <summary>
        /// Constructor
        /// </summary>
        public XboxLiveResiliencyPluginForFiddler()
        {
            InitializeMenu();
        }

        /// <summary>
        /// Create all user interface items
        /// </summary>
        private void InitializeMenu()
        {
            // Set up main menu
            rootMenuItem.MenuItems.Add(enablePluginMenuItem);
            rootMenuItem.MenuItems.Add(new MenuItem("-"));

            // Set up click event handlers
            enablePluginMenuItem.Click += new EventHandler(EnableMenuItem_Click);
            blockThisServiceMenuItem.Click += new EventHandler(BlockThisServiceMenuItem_Click);

            // Add failure types
            AddFailureTypeToMenu("404 - Not Found", FailureType.NotFound, true);
            AddFailureTypeToMenu("503 - Service Unavailable", FailureType.ServiceUnavailable);
            AddFailureTypeToMenu("429 - Rate Limited (Burst)", FailureType.RateLimitBurst);
            AddFailureTypeToMenu("429 - Rate Limited (Sustained)", FailureType.RateLimitSustained);
            rootMenuItem.MenuItems.Add(new MenuItem("-"));

            failureType = FailureType.NotFound;

            // Add serices
            MenuItem selectMenuItem = new MenuItem("Select");
            selectMenuItem.MenuItems.Add(new MenuItem("All", SelectAllOrNotMenuItem_Click));
            selectMenuItem.MenuItems.Add(new MenuItem("None", SelectAllOrNotMenuItem_Click));

            rootMenuItem.MenuItems.Add(selectMenuItem);

            AddServiceToMenu("Achievements", "achievements.xboxlive.com");
            AddServiceToMenu("Contextual Search", "contextualsearch.xboxlive.com");
            AddServiceToMenu("Data Platform", "data-vef.xboxlive.com");
            AddServiceToMenu("Leaderboards", "leaderboards.xboxlive.com");
            AddServiceToMenu("Catalog", "eds.xboxlive.com");
            AddServiceToMenu("Inventory", "inventory.xboxlive.com");
            AddServiceToMenu("Matchmaking", "smartmatch.xboxlive.com");
            AddServiceToMenu("Multiplayer Session Directory", "sessiondirectory.xboxlive.com");
            AddServiceToMenu("Presence", "userpresence.xboxlive.com");
            AddServiceToMenu("Privacy", "privacy.xboxlive.com");
            AddServiceToMenu("Profile", "profile.xboxlive.com");
            AddServiceToMenu("Realtime Activity", "rta.xboxlive.com");
            AddServiceToMenu("Social", "social.xboxlive.com");
            AddServiceToMenu("Reputation", "reputation.xboxlive.com");
            AddServiceToMenu("Client String", "client-strings.xboxlive.com");
            AddServiceToMenu("Title Storage", "titlestorage.xboxlive.com");
            AddServiceToMenu("User Stats", "userstats.xboxlive.com");
            AddServiceToMenu("External Services (non-Xbox)", ExternalServicesGuid);

            rootMenuItem.MenuItems.Add(new MenuItem("-"));

            for (int i = 1; i < rootMenuItem.MenuItems.Count; i++)
            {
                rootMenuItem.MenuItems[i].Enabled = false;
            }

            rootMenuItem.MenuItems.Add(new MenuItem("Help", HelpMenuItem_Click));
            rootMenuItem.MenuItems.Add(new MenuItem("About", AboutMenuItem_Click));
        }

        /// <summary>
        /// Add a new service to the menu
        /// </summary>
        private void AddServiceToMenu(string name, string endpoint)
        {
            services.Add(name, endpoint);

            MenuItem menuItem = new MenuItem(name, EnableMenuItem_Click);
            rootMenuItem.MenuItems.Add(menuItem);

            serviceMenuItems.Add(menuItem);
        }

        /// <summary>
        /// Add a new failure type to the menu
        /// </summary>
        private void AddFailureTypeToMenu(string name, FailureType failureType, bool check = false)
        {
            failures.Add(name, failureType);

            MenuItem menuItem = new MenuItem(name, EnableMenuItem_Click);
            menuItem.Checked = check;
            rootMenuItem.MenuItems.Add(menuItem);

            failureTypeMenuItems.Add(menuItem);
        }

        /// <summary>
        /// Block a specified host
        /// </summary>
        public void AddBlockedService(string host)
        {
            if (host.Equals(ExternalServicesGuid))
            {
                blockExternalServices = true;
            }

            if (!blockedHostList.ContainsHost(host))
            {
                string hosts = String.Format("{0}; {1}", blockedHostList, host);
                blockedHostList = new HostList(hosts);
            }
        }

        /// <summary>
        /// Unblock a specified host
        /// </summary>
        public void RemoveBlockedService(string host)
        {
            if (host.Equals(ExternalServicesGuid))
            {
                blockExternalServices = false;
            }

            if (blockedHostList.ContainsHost(host))
            {
                string hosts = blockedHostList.ToString().Replace(String.Format("{0};", host), String.Empty);
                blockedHostList = new HostList(hosts);
            }
        }

        /// <summary>
        /// Handles right-click action when "Block this Service" is selected
        /// </summary>
        private void BlockThisServiceMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                Session[] sessions = FiddlerApplication.UI.GetSelectedSessions();

                foreach (Session session in sessions)
                {
                    AddBlockedService(session.host.ToLower());
                }
            }
            catch (Exception ex)
            {
                ReportError("Could not block service: " + ex.Message);
            }
        }

        /// <summary>
        /// Displays a message box indicating the error
        /// </summary>
        private void ReportError(string error)
        {
            MessageBox.Show(error, "XboxLiveResiliencyPluginForFiddler Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// Update the status bar text in the main Fiddler window
        /// </summary>
        public void ChangeStatusText(string text)
        {
            FiddlerApplication.UI.sbpInfo.Text = text;
        }

        /// <summary>
        /// Create a Fiddler dialog with an editable textbox containing the server values
        /// </summary>
        public void EditBlockedServices()
        {
            string enteredHostValues = frmPrompt.GetUserString("Edit Blocked Host List", "Enter semicolon-delimited block list.", blockedHostList.ToString(), true);

            if (enteredHostValues == null)
                return;

            string errors;
            if (blockedHostList.AssignFromString(enteredHostValues, out errors))
            {
                ChangeStatusText("Successfully updated blocked host list.");
            }
            else
            {
                ReportError("Could not set new host values:" + errors);
            }
        }

        /// <summary>
        /// The logic for unchecking dependent options in this menu system isn't quite right, but it's fine for now.
        /// </summary>
        private void EnableMenuItem_Click(object sender, EventArgs e)
        {
            CheckForUpdate();

            MenuItem menuItem = (sender as MenuItem);

            if (menuItem.Text == enablePluginMenuItem.Text)
            {
                // Enable or disable children menu items
                menuItem.Checked = !menuItem.Checked;
                pluginEnabled = enablePluginMenuItem.Checked;

                for (int i = 1; i < rootMenuItem.MenuItems.Count - 3; i++)
                {
                    rootMenuItem.MenuItems[i].Enabled = pluginEnabled;
                }
            }
            else if (failures.ContainsKey(menuItem.Text))
            {
                // Change the current failure type
                string failure = menuItem.Text;
                failureType = failures[failure];

                foreach (MenuItem item in failureTypeMenuItems)
                {
                    item.Checked = false;
                }

                menuItem.Checked = true;
            }
            else if (services.ContainsKey(menuItem.Text))
            {
                // Select which services to block
                string serviceName = menuItem.Text;
                menuItem.Checked = !menuItem.Checked;

                // Toggle residence in blocked host list
                if (menuItem.Checked)
                {
                    AddBlockedService(services[serviceName]);
                }
                else
                {
                    RemoveBlockedService(services[serviceName]);
                }
            }
        }

        /// <summary>
        /// The logic for unchecking dependent options in this menu system isn't quite right, but it's fine for now.
        /// </summary>
        private void SelectAllOrNotMenuItem_Click(object sender, EventArgs e)
        {
            MenuItem menuItem = (sender as MenuItem);

            if (menuItem.Text.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                foreach (MenuItem item in serviceMenuItems)
                {
                    string serviceName = item.Text;
                    AddBlockedService(services[serviceName]);
                    item.Checked = true;
                }
            }
            else if (menuItem.Text.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                foreach (MenuItem item in serviceMenuItems)
                {
                    string serviceName = item.Text;
                    RemoveBlockedService(services[serviceName]);
                    item.Checked = false;
                }
            }
        }

        /// <summary>
        /// Executed on plugin load
        /// </summary>
        /// <remarks>This is not guaranteed to execute prior to an IAutoTamper method.</remarks>
        public void OnLoad()
        {
            FiddlerApplication.UI.mnuMain.MenuItems.Add(rootMenuItem);
            FiddlerApplication.UI.mnuSessionContext.MenuItems.Add(0, blockThisServiceMenuItem);
        }

        /// <summary>
        /// Executed before plugin is unloaded, typically prior to Fiddler terminating.
        /// </summary>
        public void OnBeforeUnload()
        {
            // Serialize preferences using Fiddler's built-in preferences system
            //FiddlerApplication.Prefs.SetStringPref("ext.XboxLiveResiliencyPluginForFiddler.BlockedServices", blockedHostList.ToString());
            //FiddlerApplication.Prefs.SetBoolPref("ext.XboxLiveResiliencyPluginForFiddler.EnableRateLimit", rateLimitServicesMenuItem.Checked);
        }

        /// <summary>
        /// Respond to QuickExec commands
        /// </summary>
        /// <returns>Whether the command was handled or not</returns>
        public bool OnExecAction(string command)
        {
            command = command.ToLower();

            if (!command.StartsWith("livematrix "))
                return false;

            command = command.Remove(0, 11);

            if (command.Equals("-edit", StringComparison.OrdinalIgnoreCase))
            {
                EditBlockedServices();
            }
            else
            {
                ChangeStatusText("XboxLiveResiliencyPluginForFiddler command not found.");
            }

            return true;
        }

        /// <summary>
        /// Apply a style indicating the session has been blocked to the given session
        /// </summary>
        public void MarkSession(Session session)
        {
            //session["ui-strikeout"] = "userblocked";
            session["ui-color"] = "red";
        }

        /// <summary>
        /// Executed before request modification
        /// </summary>
        public void AutoTamperRequestBefore(Session session)
        {
            // Exit early if plugin is disabled
            if (!pluginEnabled)
                return;

            string host = session.host.ToLower();

            if (blockedHostList.ContainsHost(host) || (blockExternalServices && !host.Contains("xboxlive.com")))
            {
                // Exists in C:\Program Files (x86)\Fiddler2\ResponseTemplates
                string response = "404_Plain.dat";

                // The path for these response templates can be found via CONFIG.GetPath("Responses")
                switch (failureType)
                {
                    case FailureType.NotFound:
                        response = "404_XboxLiveResiliency_NotFound.dat";
                        break;
                    case FailureType.ServiceUnavailable:
                        response = "503_XboxLiveResiliency_ServiceUnavailable.dat";
                        break;
                    case FailureType.RateLimitBurst:
                        response = "429_XboxLiveResiliency_RateLimit_Burst.dat";
                        break;
                    case FailureType.RateLimitSustained:
                        response = "429_XboxLiveResiliency_RateLimit_Sustained.dat";
                        break;
                }

                session["x-replywithfile"] = response;
                MarkSession(session);
            }
        }

        /// <summary>
        /// Executed after the user edited a response using the Fiddler Inspectors. Not called when streaming.
        /// </summary>
        public void AutoTamperResponseAfter(Session session)
        {
            // Empty
        }

        /// <summary>
        /// Executed after the user has had the chance to edit the request using the Fiddler Inspectors, but before the request is sent
        /// </summary>
        public void AutoTamperRequestAfter(Session session)
        {
            // Empty
        }

        /// <summary>
        /// Executed before the user can edit a response using the Fiddler Inspectors, unless streaming
        /// </summary>
        public void AutoTamperResponseBefore(Session session)
        {
            // Empty
        }

        /// <summary>
        /// Raised when Fiddler returns a self-generated HTTP error (e.g., DNS lookup failed, etc.)
        /// </summary>
        public void OnBeforeReturningError(Session session)
        {
            // Empty
        }

        /// <summary>
        /// Raised when a session has completed all actions
        /// </summary>
        public void OnAfterSessionComplete(Session session)
        {
            // Empty
        }

        /// <summary>
        /// Display the about window
        /// </summary>
        private void AboutMenuItem_Click(object sender, EventArgs e)
        {
            About about = new About();
            about.StartPosition = FormStartPosition.CenterScreen;
            about.Show();
        }

        /// <summary>
        /// Display the about window
        /// </summary>
        private void HelpMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start(@"https://forums.xboxlive.com");
        }

        /// <summary>
        /// Checks to see if this is the latest version. If not, prompt user to download.
        /// </summary>
        private void CheckForUpdate()
        {
            if (updateCheck)
                return;

            updateCheck = true;

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(@"http://aka.ms/livematrixversion");
                request.AllowAutoRedirect = false;

                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                if (response.StatusCode == HttpStatusCode.Moved)
                {
                    const string versionParamName = "version=";

                    string location = response.Headers["Location"];
                    int index = location.IndexOf(versionParamName, StringComparison.OrdinalIgnoreCase);

                    string parsedVersion = location.Substring(index + versionParamName.Length);
                    Version latestVersion = new Version(parsedVersion);

                    if (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version < latestVersion)
                    {
                        MessageBox.Show("You are not running the latest version of XboxLiveResiliencyPluginForFiddler. Please download the latest version from GDNP.");
                    }
                    else
                    {
                        ChangeStatusText("Running the latest version of XboxLiveResiliencyPluginForFiddler: " + latestVersion.ToString());
                    }
                }
            }
            catch (Exception)
            {
                // Empty
            }
        }
    }
}

