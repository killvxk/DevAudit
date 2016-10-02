﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Versatile;
using Alpheus;
using Alpheus.IO;

namespace DevAudit.AuditLibrary
{
    public class Drupal8Application : Application
    {
        #region Overriden properties

        public override string ApplicationId { get { return "drupal8"; } }

        public override string ApplicationLabel { get { return "Drupal 8"; } }

        public override string PackageManagerId { get { return "drupal"; } }

        public override string PackageManagerLabel { get { return "Drupal"; } }

        public override OSSIndexHttpClient HttpClient { get; } = new OSSIndexHttpClient("1.1");
       
        #endregion

        #region Public properties
        public AuditDirectoryInfo CoreModulesDirectory
        {
            get
            {
                return (AuditDirectoryInfo) this.ApplicationFileSystemMap["CoreModulesDirectory"];
            }
        }

        public AuditDirectoryInfo ContribModulesDirectory
        {
            get
            {
                return (AuditDirectoryInfo)this.ApplicationFileSystemMap["ContribModulesDirectory"];
            }
        }

        public AuditDirectoryInfo SitesAllModulesDirectory
        {
            get
            {
                IDirectoryInfo sites_all = this.RootDirectory.GetDirectories(CombinePath("sites", "all", "modules"))?.FirstOrDefault();
                {
                    return sites_all == null ? (AuditDirectoryInfo) sites_all : null;
                }
                
            }
        }

        public AuditFileInfo CorePackagesFile
        {
            get
            {
                return (AuditFileInfo) this.ApplicationFileSystemMap["CorePackagesFile"];
            }
        }
        #endregion

        #region Overriden methods
        protected override Dictionary<string, IEnumerable<OSSIndexQueryObject>> GetModules()
        {
            this.Stopwatch.Reset();
            this.Stopwatch.Start();
            AuditFileInfo changelog = this.ApplicationFileSystemMap["ChangeLog"] as AuditFileInfo;
            string[] c = changelog.ReadAsText()?.Split(this.AuditEnvironment.LineTerminator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            string core_version = "8.x";
            if (c != null && c.Count() > 0)
            {
                foreach (string l in c)
                {
                    if (l.StartsWith("Drupal "))
                    {
                        core_version = l.Split(',')[0].Substring(7);
                    }
                }
            }
            Dictionary<string, IEnumerable<OSSIndexQueryObject>> modules = new Dictionary<string, IEnumerable<OSSIndexQueryObject>>();
            List<AuditFileInfo> core_module_files = this.CoreModulesDirectory.GetFiles("*.info.yml")?.Where(f => !f.Name.Contains("_test") && !f.Name.Contains("test_")).Select(f => this.AuditEnvironment.ConstructFile(f.FullName)).ToList();
            List<AuditFileInfo> contrib_module_files = this.ContribModulesDirectory.GetFiles("*.info.yml")?.Where(f => !f.Name.Contains("_test") && !f.Name.Contains("test_")).Select(f => this.AuditEnvironment.ConstructFile(f.FullName)).ToList();       
            List<OSSIndexQueryObject> all_modules = new List<OSSIndexQueryObject>(100);
            Deserializer yaml_deserializer = new Deserializer(namingConvention: new CamelCaseNamingConvention(), ignoreUnmatched: true);
            if (core_module_files != null && core_module_files.Count > 0)
            {
                List<OSSIndexQueryObject> core_modules = new List<OSSIndexQueryObject>(core_module_files.Count + 1);
                foreach (AuditFileInfo f in core_module_files)
                {
                    string text = f.ReadAsText();
                    if (string.IsNullOrEmpty(text)) continue;
                    DrupalModuleInfo m = yaml_deserializer.Deserialize<DrupalModuleInfo>(new System.IO.StringReader(text));
                    m.ShortName = f.Name.Split('.')[0];
                    core_modules.Add(new OSSIndexQueryObject("drupal", m.ShortName, m.Version == "VERSION" ? core_version : m.Version, "", string.Empty));
                }

                modules.Add("core", core_modules);
                core_modules.Add(new OSSIndexQueryObject("drupal", "drupal_core", core_version));
                all_modules.AddRange(core_modules);
            }
            if (contrib_module_files != null && contrib_module_files.Count > 0)
            {
                List<OSSIndexQueryObject> contrib_modules = new List<OSSIndexQueryObject>(contrib_module_files.Count);
                foreach (AuditFileInfo f in contrib_module_files)
                {
                    string text = f.ReadAsText();
                    if (string.IsNullOrEmpty(text)) continue;
                    DrupalModuleInfo m = yaml_deserializer.Deserialize<DrupalModuleInfo>(new System.IO.StringReader(text));
                    m.ShortName = f.Name.Split('.')[0];
                    contrib_modules.Add(new OSSIndexQueryObject("drupal", m.ShortName, m.Version, "", string.Empty));
                }
                if (contrib_modules.Count > 0)
                {
                    modules.Add("contrib", contrib_modules);
                    all_modules.AddRange(contrib_modules);
                }
            }
            if (this.SitesAllModulesDirectory != null)
            {
                List<AuditFileInfo> sites_all_contrib_modules_files = this.SitesAllModulesDirectory.GetFiles("*.info.yml")?
                                    .Where(f => !f.Name.Contains("_test") && !f.Name.Contains("test_")).Select(f => this.AuditEnvironment.ConstructFile(f.FullName)).ToList();
                if (sites_all_contrib_modules_files!= null && sites_all_contrib_modules_files.Count > 0)
                {
                    List<OSSIndexQueryObject> sites_all_contrib_modules = new List<OSSIndexQueryObject>(sites_all_contrib_modules_files.Count + 1);
                    foreach (AuditFileInfo f in sites_all_contrib_modules_files)
                    {
                        string text = f.ReadAsText();
                        if (string.IsNullOrEmpty(text)) continue;
                        DrupalModuleInfo m = yaml_deserializer.Deserialize<DrupalModuleInfo>(new System.IO.StringReader(text));
                        m.ShortName = f.Name.Split('.')[0];
                        sites_all_contrib_modules.Add(new OSSIndexQueryObject("drupal", m.ShortName, m.Version, "", string.Empty));
                    }
                    if (sites_all_contrib_modules.Count > 0)
                    {
                        modules.Add("sites_all_contrib", sites_all_contrib_modules);
                        all_modules.AddRange(sites_all_contrib_modules);
                    }
                }
            }
            modules.Add("all", all_modules);
            this.Stopwatch.Stop();
            this.AuditEnvironment.Success("Got {0} {1} modules in {2} ms.", modules["all"].Count(), this.ApplicationLabel, this.Stopwatch.ElapsedMilliseconds);
            return modules;
        }

        public override IEnumerable<OSSIndexQueryObject> GetPackages(params string[] o)
        {
            return this.GetModules()["all"];
        }

        protected override IConfiguration GetConfiguration()
        {
            throw new NotImplementedException();
        }

        public override Func<List<OSSIndexArtifact>, List<OSSIndexArtifact>> ArtifactsTransform { get; } = (artifacts) =>
        {
            List<OSSIndexArtifact> o = artifacts.ToList();
            foreach (OSSIndexArtifact a in o)
            {
                if (a.Search == null || a.Search.Count() != 4)
                {
                    throw new Exception("Did not receive expected Search field properties for artifact name: " + a.PackageName + " id: " +
                        a.PackageId + " project id: " + a.ProjectId + ".");
                }
                else
                {
                    OSSIndexQueryObject package = new OSSIndexQueryObject(a.Search[0], a.Search[1], a.Search[3], "");
                    a.Package = package;
                }
            }
            return o;
        };

        public override bool IsConfigurationRuleVersionInServerVersionRange(string configuration_rule_version, string server_version)
        {
            return (configuration_rule_version == server_version) || configuration_rule_version == ">0";
        }

        public override bool IsVulnerabilityVersionInPackageVersionRange(string vulnerability_version, string package_version)
        {
            string message = "";
            bool r = Drupal.RangeIntersect(vulnerability_version, package_version, out message);
            if (!r && !string.IsNullOrEmpty(message))
            {
                throw new Exception(message);
            }
            else return r;
        }

        #endregion

        #region Constructors
        public Drupal8Application(Dictionary<string, object> application_options, EventHandler<EnvironmentEventArgs> message_handler = null) : base(application_options, new Dictionary<string, string[]>()
            {
                { "ChangeLog", new string[] { "@", "core", "CHANGELOG.txt" } },
                { "CorePackagesFile", new string[] { "@", "core", "composer.json" } }
            }, new Dictionary<string, string[]>()
            {
                { "CoreModulesDirectory", new string[] { "@", "core", "modules" } },
                { "ContribModulesDirectory", new string[] { "@", "modules" } },
                { "DefaultSiteDirectory", new string[] { "@", "sites", "default" } }
            }, message_handler)
        {}
        #endregion

    }
}
