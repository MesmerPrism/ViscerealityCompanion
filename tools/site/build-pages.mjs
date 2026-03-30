import { promises as fs } from "node:fs";
import path from "node:path";
import { marked } from "marked";
import YAML from "yaml";

const rootDir = process.cwd();
const docsDir = path.join(rootDir, "docs");
const siteDir = path.join(rootDir, "site");
const assetsDir = path.join(docsDir, "assets");

await fs.rm(siteDir, { recursive: true, force: true });
await fs.mkdir(siteDir, { recursive: true });
await copyDirectory(assetsDir, path.join(siteDir, "assets"));

const markdownFiles = await listMarkdownFiles(docsDir);
const pages = [];

for (const sourcePath of markdownFiles) {
    const relativePath = path.relative(docsDir, sourcePath);
    const sourceText = await fs.readFile(sourcePath, "utf8");
    const { frontmatter, body } = parseFrontmatter(sourceText);
    const title = frontmatter.title ?? firstHeading(body) ?? path.basename(relativePath, ".md");
    const description = frontmatter.description ?? "";
    const summary = frontmatter.summary ?? description;
    const navLabel = frontmatter.nav_label ?? title;
    const navGroup = frontmatter.nav_group ?? "Docs";
    const navOrder = Number(frontmatter.nav_order ?? 999);
    const outputRelative = relativePath.replace(/\.md$/i, ".html");
    const outputPath = path.join(siteDir, outputRelative);

    pages.push({
        title,
        description,
        summary,
        navLabel,
        navGroup,
        navOrder,
        sourcePath,
        relativePath,
        outputRelative,
        outputPath,
        html: rewriteMarkdownLinks(marked.parse(body))
    });
}

pages.sort((left, right) => left.navOrder - right.navOrder || left.title.localeCompare(right.title));

for (const page of pages) {
    await fs.mkdir(path.dirname(page.outputPath), { recursive: true });
    const html = renderPage(page, pages);
    await fs.writeFile(page.outputPath, html, "utf8");
}

function parseFrontmatter(text) {
    if (!text.startsWith("---")) {
        return { frontmatter: {}, body: text };
    }

    const closingIndex = text.indexOf("\n---", 3);
    if (closingIndex === -1) {
        return { frontmatter: {}, body: text };
    }

    const yamlText = text.slice(3, closingIndex).trim();
    const body = text.slice(closingIndex + 4).trimStart();
    return {
        frontmatter: YAML.parse(yamlText) ?? {},
        body
    };
}

function firstHeading(body) {
    const match = body.match(/^#\s+(.+)$/m);
    return match?.[1]?.trim() ?? null;
}

function rewriteMarkdownLinks(html) {
    return html.replace(/href="([^":]+)\.md"/g, 'href="$1.html"');
}

function renderPage(page, pages) {
    const homePage = pages.find(candidate => candidate.relativePath === "index.md") ?? pages[0];
    const groupedPages = groupPages(pages);
    const navSections = groupedPages
        .map(([groupName, entries]) => {
            const items = entries
                .map(candidate => {
                    const relativeHref = path.relative(path.dirname(page.outputPath), candidate.outputPath).replace(/\\/g, "/");
                    const activeClass = candidate.outputPath === page.outputPath ? " active" : "";
                    const active = candidate.outputPath === page.outputPath ? ' aria-current="page"' : "";
                    return `<a class="nav-item${activeClass}" href="${relativeHref}"${active}><strong>${escapeHtml(candidate.navLabel)}</strong><span class="desc">${escapeHtml(candidate.summary)}</span></a>`;
                })
                .join("");

            return `<section><h2>${escapeHtml(groupName)}</h2><div class="nav-group">${items}</div></section>`;
        })
        .join("");

    const assetPrefix = path.relative(path.dirname(page.outputPath), siteDir).replace(/\\/g, "/") || ".";
    const stylesheetHref = `${assetPrefix}/assets/site.css`;
    const brandMarkHref = `${assetPrefix}/assets/viscereality-mark.png`;
    const brandWordmarkHref = `${assetPrefix}/assets/viscereality-wordmark.png`;
    const homeHref = path.relative(path.dirname(page.outputPath), homePage.outputPath).replace(/\\/g, "/");
    const downloadPage = pages.find(candidate => candidate.relativePath === "download.md");
    const firstSessionPage = pages.find(candidate => candidate.relativePath === "first-session.md");
    const gettingStartedPage = pages.find(candidate => candidate.relativePath === "getting-started.md");
    const studyShellsPage = pages.find(candidate => candidate.relativePath === "study-shells.md");
    const topNav = [
        homePage,
        downloadPage,
        firstSessionPage,
        studyShellsPage
    ]
        .filter(Boolean)
        .map(candidate => {
            const relativeHref = path.relative(path.dirname(page.outputPath), candidate.outputPath).replace(/\\/g, "/");
            const activeClass = candidate.outputPath === page.outputPath ? " active" : "";
            return `<a class="${activeClass.trim()}" href="${relativeHref}">${escapeHtml(candidate.navLabel)}</a>`;
        })
        .join("");
    const onboardingLinks = [
        downloadPage,
        firstSessionPage,
        studyShellsPage
    ]
        .filter(Boolean)
        .map(candidate => {
            const relativeHref = path.relative(path.dirname(page.outputPath), candidate.outputPath).replace(/\\/g, "/");
            return `<a class="aside-link" href="${relativeHref}">${escapeHtml(candidate.navLabel)}</a>`;
        })
        .join("");

    return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${escapeHtml(page.title)} | Viscereality Companion</title>
  <meta name="description" content="${escapeHtml(page.description)}">
  <link rel="stylesheet" href="${stylesheetHref}">
</head>
<body>
  <div class="site-shell">
    <header class="site-header">
      <a class="brand" href="${homeHref}">
        <img class="brand-mark" src="${brandMarkHref}" alt="Viscereality Companion mark">
        <div class="brand-copy">
          <img class="brand-wordmark" src="${brandWordmarkHref}" alt="Altered States of Viscereality">
          <span>Companion desktop operator surface</span>
        </div>
      </a>
      <nav class="top-nav">${topNav}</nav>
    </header>

    <section class="hero">
      <div class="hero-copy panel">
        <p class="kicker">${escapeHtml(page.navGroup)}</p>
        <h1>${escapeHtml(page.title)}</h1>
        <p class="page-intro">${escapeHtml(page.summary)}</p>
      </div>
      <aside class="hero-aside panel">
        <h2>New here?</h2>
        <ol class="quick-steps">
          <li>Install the Windows preview package or guided setup helper.</li>
          <li>Make sure the Quest is in developer mode, then approve USB debugging once on that headset.</li>
          <li>The Sussex package already includes the Sussex APK, device profile, and study shell, so you can connect Quest and run the session from Windows without a separate APK handoff.</li>
        </ol>
        <div class="aside-links">${onboardingLinks}</div>
        <p class="aside-note">This repo is the public Windows operator surface. The Unity runtime and study APK development stay in AstralKarateDojo.</p>
      </aside>
    </section>

    <main class="page-layout">
      <nav class="sidebar">
        ${navSections}
      </nav>
      <article class="content-panel prose">${page.html}</article>
    </main>
  </div>
</body>
</html>`;
}

function groupPages(pages) {
    const groups = new Map();

    for (const page of pages) {
        if (!groups.has(page.navGroup)) {
            groups.set(page.navGroup, []);
        }

        groups.get(page.navGroup).push(page);
    }

    return Array.from(groups.entries());
}

async function listMarkdownFiles(directory) {
    const entries = await fs.readdir(directory, { withFileTypes: true });
    const files = [];

    for (const entry of entries) {
        const fullPath = path.join(directory, entry.name);
        if (entry.isDirectory()) {
            files.push(...await listMarkdownFiles(fullPath));
            continue;
        }

        if (entry.isFile() && entry.name.endsWith(".md")) {
            files.push(fullPath);
        }
    }

    return files;
}

async function copyDirectory(source, target) {
    await fs.mkdir(target, { recursive: true });
    const entries = await fs.readdir(source, { withFileTypes: true });

    for (const entry of entries) {
        const sourcePath = path.join(source, entry.name);
        const targetPath = path.join(target, entry.name);

        if (entry.isDirectory()) {
            await copyDirectory(sourcePath, targetPath);
        } else {
            await fs.copyFile(sourcePath, targetPath);
        }
    }
}

function escapeHtml(value) {
    return value
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;");
}
