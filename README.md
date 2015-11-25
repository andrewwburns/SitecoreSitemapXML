SitecoreSitemapXML
==================

The Sitemap XML module generates the Sitemap compliant with the schema defined by sitemaps.org and submits it to search engines. 

Modified by Andy Burns from 3chillies, 2015
-------------------------------------------
- added support for '<site>' node default language
- added multi-lingual sitemap support as per https://support.google.com/webmasters/answer/2620865?hl=en 
- Fixed some error handling
- Fixed URL generation, which was distinctly screwy
- Fixed Ping service to use full URL, not relative
- Fixed Sitemap entries in Robots.txt to be absolute, not relative (as per spec)

Manually Updating an installed Sitemap module
----------------------------------------------------------
-	Copy the SitemapXML.dll into the website BIN directory. Note that this will recycle your app pool.
-	In Web.config, check the default language setting on the <site> node. 
-	Update SitemapXML.config . Note that changes will recycle your app pool.
    + *[Optional]* Remove the `<sitemapVariable name="xmlnsTpl” /> `node. It’s not necessary.
	+ *[Optional]* On your Site nodes, specify your languages. E.g. `<site name="website" filename="sitemap.xml" serverUrl="www.example.com" languages="en-GB,fr-FR,de-DE " />`
	+ Check your ‘ProductionEnvironment’ setting.
-	Update the nodes for the search services you’re going to ping. 
	+ Remove Yahoo. You don’t need it, it uses Bing now.
	+ Update Live Search to use Bing. Change the Name, and set the URL to: http://www.bing.com/webmaster/ping.aspx?siteMap=
-	Make sure that your Robots.txt file does not contain any relative paths to the sitemap.xml

Publish some content and the Sitemap file (and Robots.txt) should update.