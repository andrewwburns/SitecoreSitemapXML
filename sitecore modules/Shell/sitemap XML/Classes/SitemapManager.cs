/* *********************************************************************** *
 * File   : SitemapManager.cs                             Part of Sitecore *
 * Version: 1.5.0                                         www.sitecore.net *
 *                                                                         *
 *                                                                         *
 * Purpose: Manager class what contains all main logic                     *
 *                                                                         *
 * Copyright (C) 1999-2009 by Sitecore A/S. All rights reserved.           *
 *                                                                         *
 * This work is the property of:                                           *
 *                                                                         *
 *        Sitecore A/S                                                     *
 *        Meldahlsgade 5, 4.                                               *
 *        1613 Copenhagen V.                                               *
 *        Denmark                                                          *
 *                                                                         *
 * This is a Sitecore published work under Sitecore's                      *
 * shared source license.                                                  *
 *                                                                         *
 * *********************************************************************** */

/* Modified by 3chillies, 2015.
- added support for '<site>' node default language
- added multi-lingual sitemap support as per https://support.google.com/webmasters/answer/2620865?hl=en 
- Fixed error handling
- Fixed URL generation, which was distinctly screwy
- Fixed Ping service to use full URL, not relative
- Fixed Sitemap entries in Robots.txt to be absolute, not relative (as per spec)
*/


using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Data.Managers;
using Sitecore.Diagnostics;
using Sitecore.Globalization;
using Sitecore.Links;
using Sitecore.Sites;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;

namespace Sitecore.Modules.SitemapXML
{
    public class SitemapManager
    {
        //private static string sitemapUrl;

        private static StringDictionary m_Sites;
        public Database Db
        {
            get
            {
                Database database = Factory.GetDatabase(SitemapManagerConfiguration.WorkingDatabase);
                return database;
            }
        }

        public SitemapManager()
        {
            m_Sites = SitemapManagerConfiguration.GetSites();
            foreach (DictionaryEntry site in m_Sites)
            {
                BuildSiteMap(site.Key.ToString(), site.Value.ToString());
            }
        }


        private void BuildSiteMap(string sitename, string sitemapUrlNew)
        {
            Site site = Sitecore.Sites.SiteManager.GetSite(sitename);
            SiteContext siteContext = Factory.GetSite(sitename);
            string rootPath = siteContext.StartPath;

            List<Item> items = GetSitemapItems(rootPath);


            string fullPath = MainUtil.MapPath(string.Concat("/", sitemapUrlNew));
            string xmlContent = this.BuildSitemapXML(items, site);

            StreamWriter strWriter = new StreamWriter(fullPath, false);
            strWriter.Write(xmlContent);
            strWriter.Close();

        }

        public bool SubmitSitemapToSearchenginesByHttp()
        {
            if (!SitemapManagerConfiguration.IsProductionEnvironment)
                return false;

            bool result = false;
            Item sitemapConfig = Db.Items[SitemapManagerConfiguration.SitemapConfigurationItemPath];

            if (sitemapConfig != null)
            {
                string engines = sitemapConfig.Fields["Search engines"].Value;
                foreach (string id in engines.Split('|'))
                {
                    Item engine = Db.Items[id];
                    if (engine != null)
                    {
                        string engineHttpRequestString = engine.Fields["HttpRequestString"].Value;

                        foreach (DictionaryEntry site in m_Sites)
                        {
                            string sitename = site.Key.ToString();
                            string serverUrl = SitemapManagerConfiguration.GetServerUrlBySite(sitename);
                            if (string.IsNullOrEmpty(serverUrl))
                            {
                                serverUrl = ResolveSiteUrl(sitename);
                            }
                            string sitemapUrl = site.Value.ToString();
                            this.SubmitEngine(engineHttpRequestString, string.Format("http://{0}/{1}", serverUrl, sitemapUrl));
                        }
                    }
                }
                result = true;
            }

            return result;
        }

        public void RegisterSitemapToRobotsFile()
        {

            string robotsPath = MainUtil.MapPath(string.Concat("/", "robots.txt"));
            StringBuilder sitemapContent = new StringBuilder(string.Empty);
            if (File.Exists(robotsPath))
            {
                StreamReader sr = new StreamReader(robotsPath);
                sitemapContent.Append(sr.ReadToEnd());
                sr.Close();
            }

            StreamWriter sw = new StreamWriter(robotsPath, false);
            foreach (DictionaryEntry site in m_Sites)
            {
                string sitename = site.Key.ToString();

                string serverUrl = SitemapManagerConfiguration.GetServerUrlBySite(sitename);
                if (string.IsNullOrEmpty(serverUrl))
                {
                    serverUrl = ResolveSiteUrl( sitename);
                }

                string sitemapLine = string.Format("Sitemap: http://{0}/{1}", serverUrl, site.Value);
                if (!sitemapContent.ToString().Contains(sitemapLine))
                {
                    sitemapContent.AppendLine(sitemapLine);
                }
            }
            sw.Write(sitemapContent.ToString());
            sw.Close();
        }

        private string ResolveSiteUrl(string sitename)
        {
            Sitecore.Links.UrlOptions options = Sitecore.Links.UrlOptions.DefaultOptions;
            options.SiteResolving = Sitecore.Configuration.Settings.Rendering.SiteResolving;
            options.Site = SiteContext.GetSite(sitename);
            options.AlwaysIncludeServerUrl = true;

            SiteContext ctx = Sitecore.Configuration.Factory.GetSite(sitename);
            Item startItem = Db.GetItem(ctx.StartPath);
            string serverUrl = Sitecore.Links.LinkManager.GetItemUrl(startItem, options);

            return Regex.Replace(serverUrl, "^https?://([^/]+)/.*", "$1");
        }

        private string BuildSitemapXML(List<Item> items, Site site)
        {
            XmlDocument doc = new XmlDocument();

            XmlNode declarationNode = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
            doc.AppendChild(declarationNode);

            XmlNode urlsetNode = doc.CreateElement("urlset");
            doc.AppendChild(urlsetNode);

            doc.DocumentElement.SetAttribute("xmlns", "http://www.sitemaps.org/schemas/sitemap/0.9");
            doc.DocumentElement.SetAttribute("xmlns:xhtml", "http://www.w3.org/1999/xhtml");


            //System default Language - in Web.config Settings section
            string defaultlang = LanguageManager.DefaultLanguage.ToString();

            //Site default Language - in <site> definition in Site.config
            if (site.Properties.ContainsKey("language"))
            {
                defaultlang = site.Properties["language"];
            }

            List<string> desiredLangs = SitemapManagerConfiguration.GetLangsBySite(site.Name);

            foreach (Item itm in items)
            {
                doc = this.BuildSitemapItem(doc, itm, site, defaultlang, desiredLangs);
            }

            return doc.OuterXml;
        }

        private XmlDocument BuildSitemapItem(XmlDocument doc, Item item, Site site, string defaultlang, List<string> desiredLangs)
        {

            string url = HtmlEncode(this.GetItemUrl(item, site, LanguageEmbedding.Always, defaultlang));

            DateTime lastMod = item.Statistics.Updated;
            lastMod = (lastMod < item.Statistics.Created) ? item.Statistics.Created : lastMod;

            XmlNode urlsetNode = doc.LastChild;

            XmlNode urlNode = doc.CreateElement("url");
            urlsetNode.AppendChild(urlNode);

            XmlNode locNode = doc.CreateElement("loc");
            urlNode.AppendChild(locNode);
            locNode.AppendChild(doc.CreateTextNode(url));

            foreach (Language itemLanguage in item.Languages)
            {
                string lang = itemLanguage.ToString();
                
                //All languages desired
                //Or default language
                //Or in list of desired languages
                if ((desiredLangs.Count < 1) || (lang.Equals(defaultlang, StringComparison.InvariantCultureIgnoreCase)) || desiredLangs.Contains(lang.ToLower()))
                {

                    var langItem = item.Database.GetItem(item.ID, itemLanguage);
                    if (langItem.Versions.Count > 0)
                    {
                        string altUrl = GetItemUrl(langItem, site, LanguageEmbedding.Always, lang);

                        XmlElement altLangNode = doc.CreateElement("xhtml", "link", "http://www.w3.org/1999/xhtml");
                        altLangNode.SetAttribute("rel", "alternate");
                        altLangNode.SetAttribute("hreflang", lang);
                        altLangNode.SetAttribute("href", altUrl);
                        urlNode.AppendChild(altLangNode);

                        lastMod = (lastMod < item.Statistics.Updated) ? item.Statistics.Updated : lastMod;
                    }
                }
            }

            XmlNode lastmodNode = doc.CreateElement("lastmod");
            urlNode.AppendChild(lastmodNode);
            lastmodNode.AppendChild(doc.CreateTextNode(HtmlEncode(lastMod.ToString("yyyy-MM-ddTHH:mm:sszzz"))));

            return doc;
        }

        private string GetItemUrl(Item item, Site site, LanguageEmbedding langEmbed, string lang)
        {
            lang = lang.Trim();
            string url = null;

            if (LanguageManager.IsValidLanguageName(lang))
            {

                string serverUrl = SitemapManagerConfiguration.GetServerUrlBySite(site.Name);

                Sitecore.Links.UrlOptions options = Sitecore.Links.UrlOptions.DefaultOptions;
                options.SiteResolving = Sitecore.Configuration.Settings.Rendering.SiteResolving;
                options.Site = SiteContext.GetSite(site.Name);
                options.AlwaysIncludeServerUrl = string.IsNullOrEmpty(serverUrl);
                options.LanguageEmbedding = langEmbed;
                options.Language = LanguageManager.GetLanguage(lang);

                url = Sitecore.Links.LinkManager.GetItemUrl(item, options);

                if (!string.IsNullOrEmpty(serverUrl))
                {
                    url = string.Format("http://{0}{1}", serverUrl, url);
                }
            } else
            {
                Log.Warn(string.Format("Language '{0}' is not valid", lang), this);
            }
            
            return url;

        }

        private static string HtmlEncode(string text)
        {
            string result = HttpUtility.HtmlEncode(text);

            return result;
        }

        private void SubmitEngine(string engine, string sitemapUrl)
        {
            //Check if it is not localhost because search engines returns an error
            if (!sitemapUrl.Contains("localhost"))
            {
                string request = string.Concat(engine, HtmlEncode(sitemapUrl));

                System.Net.HttpWebRequest httpRequest = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(request);
                try
                {
                    System.Net.WebResponse webResponse = httpRequest.GetResponse();

                    System.Net.HttpWebResponse httpResponse = (System.Net.HttpWebResponse)webResponse;
                    if (httpResponse.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        Log.Error(string.Format("Unsuccessful submit sitemap to \"{0}\" - {1}", request, httpResponse.StatusCode), this);
                    }
                }
                catch (WebException ex)
                {
                    HttpWebResponse resp = ex.Response as HttpWebResponse;
                    string msg = string.Format("[{0}] Cannot submit sitemap to \"{1}\" - {2}", ex.GetType().ToString(), request, ex.Message);
                    if (resp != null)
                    {
                        msg += string.Format(" (Status: {0})", resp.StatusCode);
                    }

                    Log.Error(msg, this);
                }
                catch( Exception ex2)
                {
                    Log.Error(string.Format("[{0}] Cannot submit sitemap to \"{1}\" - {2}", ex2.GetType().ToString(), request, ex2.Message), this);
                }
            }
        }


        private List<Item> GetSitemapItems(string rootPath)
        {
            string disTpls = SitemapManagerConfiguration.EnabledTemplates;
            string exclNames = SitemapManagerConfiguration.ExcludeItems;


            Database database = Factory.GetDatabase(SitemapManagerConfiguration.WorkingDatabase);

            Item contentRoot = database.Items[rootPath];

            Item[] descendants;
            Sitecore.Security.Accounts.User user = Sitecore.Security.Accounts.User.FromName(@"extranet\Anonymous", true);
            using (new Sitecore.Security.Accounts.UserSwitcher(user))
            {
                descendants = contentRoot.Axes.GetDescendants();
            }
            List<Item> sitemapItems = descendants.ToList();
            sitemapItems.Insert(0, contentRoot);

            List<string> enabledTemplates = this.BuildListFromString(disTpls, '|');
            List<string> excludedNames = this.BuildListFromString(exclNames, '|');


            var selected = from itm in sitemapItems
                           where itm.Template != null && enabledTemplates.Contains(itm.Template.ID.ToString()) &&
                                    !excludedNames.Contains(itm.ID.ToString())
                           select itm;

            return selected.ToList();
        }

        private List<string> BuildListFromString(string str, char separator)
        {
            string[] enabledTemplates = str.Split(separator);
            var selected = from dtp in enabledTemplates
                           where !string.IsNullOrEmpty(dtp)
                           select dtp;

            List<string> result = selected.ToList();

            return result;
        }

    }
}
