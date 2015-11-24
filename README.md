SitecoreSitemapXML
==================

The Sitemap XML module generates the Sitemap compliant with the schema defined by sitemaps.org and submits it to search engines. 

Modified by Andy Burns from 3chillies, 2015
- added support for '<site>' node default language
- added multi-lingual sitemap support as per https://support.google.com/webmasters/answer/2620865?hl=en 
- Fixed some error handling
- Fixed URL generation, which was distinctly screwy
- Fixed Ping service to use full URL, not relative
- Fixed Sitemap entries in Robots.txt to be absolute, not relative (as per spec)
