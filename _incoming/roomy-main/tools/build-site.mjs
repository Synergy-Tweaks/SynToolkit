// Generates the guide pages, guides index, 404 page and sitemap for site/.
// Usage: node tools/build-site.mjs
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { guides } from "./guides.mjs";

const SITE = join(dirname(fileURLToPath(import.meta.url)), "..", "site");
const BASE = "https://roomy.krezio.xyz";
const UPDATED = "2026-11-12";
const DOWNLOAD = "https://github.com/krezio/roomy/releases/latest/download/Roomy-Setup.exe";

const esc = (s) => s.replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/</g, "&lt;");

function head({ title, description, url, root, jsonld, type = "website" }) {
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${esc(title)}</title>
  <meta name="description" content="${esc(description)}">
  <link rel="canonical" href="${url}">
  <meta name="robots" content="index, follow, max-image-preview:large">
  <meta name="theme-color" content="#111113">
  <link rel="icon" href="${root}favicon.ico" sizes="any">
  <link rel="icon" href="${root}assets/logo.svg" type="image/svg+xml">
  <link rel="apple-touch-icon" href="${root}assets/apple-touch-icon.png">
  <meta property="og:type" content="${type}">
  <meta property="og:site_name" content="Roomy">
  <meta property="og:title" content="${esc(title)}">
  <meta property="og:description" content="${esc(description)}">
  <meta property="og:url" content="${url}">
  <meta property="og:image" content="${BASE}/assets/og.png">
  <meta name="twitter:card" content="summary_large_image">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&display=swap" rel="stylesheet">
  <link rel="stylesheet" href="${root}styles.css">
${jsonld ? `  <script type="application/ld+json">${JSON.stringify(jsonld)}</script>\n` : ""}</head>
<body>
  <header class="nav-wrap">
    <nav class="nav" aria-label="Main">
      <a class="brand" href="${root}"><img src="${root}assets/logo.svg" alt="" width="24" height="24"><span>Roomy</span></a>
      <div class="nav-links">
        <a href="${root}#try">Try it</a>
        <a href="${root}#finds">What it finds</a>
        <a href="${root}guides/">Guides</a>
      </div>
      <a class="btn btn-light btn-sm" href="${root}#download">Download</a>
    </nav>
  </header>
`;
}

function foot(root) {
  return `
  <footer class="footer">
    <span class="brand"><img src="${root}assets/logo.svg" alt="" width="18" height="18"><span>Roomy</span></span>
    <span>Made by <a href="https://krezio.xyz">krezio</a> · <a href="https://github.com/krezio/roomy" target="_blank" rel="noopener">GitHub</a> · <a href="https://framecalm.krezio.xyz">Framecalm</a> · <a href="${root}guides/">Guides</a> · <a href="${root}privacy.html">Privacy</a></span>
  </footer>
  <script defer src="https://static.cloudflareinsights.com/beacon.min.js" data-cf-beacon='{"token": "595ddd43099c44e98b23cda95087c4b7"}'></script>
</body>
</html>
`;
}

const cta = (root) => `
    <aside class="cta-box">
      <img src="${root}assets/logo.svg" alt="" width="52" height="52">
      <div>
        <h2>Or let Roomy do it in one click</h2>
        <p>Roomy finds all of this for you, explains each item in plain English, and only cleans what's safe. Free for Windows 11 and 10, no ads, no account.</p>
      </div>
      <a class="btn btn-light" href="${DOWNLOAD}">Download Roomy free</a>
    </aside>`;

function write(rel, html) {
  const file = join(SITE, rel);
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, html);
}

// ---------- guide pages ----------
for (const g of guides) {
  const url = `${BASE}/guides/${g.slug}/`;
  const root = "../../";
  const others = guides.filter((o) => o.slug !== g.slug).slice(0, 4);
  const jsonld = {
    "@context": "https://schema.org",
    "@graph": [
      {
        "@type": "Article",
        headline: g.title,
        description: g.description,
        url,
        mainEntityOfPage: url,
        datePublished: UPDATED,
        dateModified: UPDATED,
        image: `${BASE}/assets/og.png`,
        author: { "@type": "Organization", name: "krezio", url: "https://krezio.xyz" },
        publisher: { "@type": "Organization", name: "krezio", logo: { "@type": "ImageObject", url: `${BASE}/assets/icon-512.png` } },
      },
      {
        "@type": "BreadcrumbList",
        itemListElement: [
          { "@type": "ListItem", position: 1, name: "Roomy", item: `${BASE}/` },
          { "@type": "ListItem", position: 2, name: "Guides", item: `${BASE}/guides/` },
          { "@type": "ListItem", position: 3, name: g.nav, item: url },
        ],
      },
    ],
  };
  const body = g.body.replace(/href="\.\.\/([a-z-]+)\/"/g, `href="../$1/"`);
  write(`guides/${g.slug}/index.html`,
    head({ title: g.title, description: g.description, url, root, jsonld, type: "article" }) + `
  <main class="doc">
    <nav class="crumbs" aria-label="Breadcrumb"><a href="${root}">Roomy</a> › <a href="../">Guides</a></nav>
    <article>
      <h1>${g.title}</h1>
      <p class="meta">Updated November 2026 · ${g.minutes} min read</p>
      <div class="tldr"><span class="label">Short answer</span><p>${g.tldr}</p></div>
${body}
    </article>
${cta(root)}
    <h2>More guides</h2>
    <ul class="related">
${others.map((o) => `      <li><a href="../${o.slug}/">${o.nav}</a></li>`).join("\n")}
    </ul>
  </main>
` + foot(root));
}

// ---------- guides index ----------
write("guides/index.html",
  head({
    title: "Free up space on Windows: guides | Roomy",
    description: "Plain-English guides to freeing up disk space on Windows 11 and 10: C: drive full, temp files, shader cache, Windows.old, Windows Update files and more.",
    url: `${BASE}/guides/`,
    root: "../",
    jsonld: {
      "@context": "https://schema.org",
      "@type": "CollectionPage",
      name: "Roomy guides",
      url: `${BASE}/guides/`,
      hasPart: guides.map((g) => ({ "@type": "Article", headline: g.title, url: `${BASE}/guides/${g.slug}/` })),
    },
  }) + `
  <main class="doc">
    <nav class="crumbs" aria-label="Breadcrumb"><a href="../">Roomy</a> › Guides</nav>
    <h1>Free up space on Windows</h1>
    <p class="meta">Plain-English guides. Each one shows how to do it yourself, step by step.</p>
    <ul class="guide-list">
${guides.map((g) => `      <li><a href="${g.slug}/"><h2>${g.nav}</h2><p>${g.description}</p></a></li>`).join("\n")}
    </ul>
${cta("../")}
  </main>
` + foot("../"));

// ---------- 404 ----------
write("404.html",
  head({ title: "Page not found | Roomy", description: "This page doesn't exist.", url: `${BASE}/404.html`, root: "/" })
    .replace('<meta name="robots" content="index, follow, max-image-preview:large">', '<meta name="robots" content="noindex">') + `
  <main class="doc" style="text-align:center">
    <h1>Nothing here</h1>
    <p>This page doesn't exist, but there's plenty of free space on it.</p>
    <p><a class="btn btn-light" href="/">Back to Roomy</a> <a class="btn btn-dark" href="/guides/">Read the guides</a></p>
  </main>
` + foot("/"));

// ---------- sitemap ----------
const urls = [
  { loc: `${BASE}/`, priority: "1.0" },
  { loc: `${BASE}/guides/`, priority: "0.8" },
  ...guides.map((g) => ({ loc: `${BASE}/guides/${g.slug}/`, priority: "0.7" })),
  { loc: `${BASE}/privacy.html`, priority: "0.3" },
];
write("sitemap.xml", `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${urls.map((u) => `  <url><loc>${u.loc}</loc><lastmod>${UPDATED}</lastmod><priority>${u.priority}</priority></url>`).join("\n")}
</urlset>
`);

console.log(`Built ${guides.length} guides, guides index, 404 and sitemap (${urls.length} URLs).`);
