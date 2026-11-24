// Guide content for roomy.krezio.xyz/guides. Bodies are plain HTML.
// Run `node tools/build-site.mjs` after editing.

export const guides = [
  {
    slug: "c-drive-full",
    title: "C: drive full on Windows 11 or 10? 8 safe ways to free up space",
    nav: "C: drive full? 8 safe ways to free up space",
    description: "Your C: drive is full and Windows is complaining. Here are 8 safe ways to free up space on Windows 11 and 10, what's fine to delete, and what you should never touch.",
    minutes: 7,
    tldr: "Start with the Recycle Bin, Windows' own <b>Temporary files</b> cleanup, and your Downloads folder. Then uninstall games and apps you don't use. Never delete things from <code>C:\\Windows</code>, <code>WinSxS</code> or <code>Program Files</code> by hand.",
    body: `
<p>When the C: drive bar in File Explorer turns red, Windows starts misbehaving: updates fail, apps crash, and games refuse to install. The good news is that most PCs have 10–40 GB of safe-to-delete junk lying around. Here's how to find it, roughly in order of how much space each step usually frees.</p>

<h2>1. Empty the Recycle Bin</h2>
<p>Deleted files still take up space until the bin is emptied. Right-click the <b>Recycle Bin</b> on your desktop and choose <b>Empty Recycle Bin</b>. Obvious, but it's often several gigabytes.</p>

<h2>2. Use Windows' "Temporary files" cleanup</h2>
<p>This is the single biggest win on most PCs, because it includes Windows Update leftovers.</p>
<ol>
  <li>Open <b>Settings → System → Storage</b>.</li>
  <li>Click <b>Temporary files</b> and wait for it to scan.</li>
  <li>Tick <b>Windows Update Cleanup</b>, <b>Delivery Optimization Files</b>, <b>Temporary files</b>, <b>DirectX Shader Cache</b> and <b>Thumbnails</b>.</li>
  <li>Leave <b>Downloads</b> unticked unless you've checked what's in there, then click <b>Remove files</b>.</li>
</ol>
<p>On Windows 10 the old <b>Disk Cleanup</b> tool does the same job: search for it, then click <b>Clean up system files</b> to see the Windows Update options. See also: <a href="../windows-update-files/">is it safe to delete Windows Update files?</a></p>

<h2>3. Clean out your Downloads folder</h2>
<p>Downloads is where installers, ZIPs and videos go to be forgotten. Open it, switch to <b>Details</b> view and sort by <b>Size</b>. Setup files like <code>something-setup.exe</code> or <code>.msi</code> files are safe to delete once the app is installed.</p>

<h2>4. Uninstall games and apps you don't use</h2>
<p>Go to <b>Settings → Apps → Installed apps</b> and sort by <b>Size</b>. Games are usually the biggest items on any PC; a single modern game can be 70–150 GB. Uninstall through Steam, Epic or the Xbox app so you can reinstall later with one click.</p>

<h2>5. Clear browser and app caches</h2>
<p>Chrome, Edge, Discord, Spotify, VS Code and Steam all keep caches that grow over time. Clearing a cache is safe: the app just re-downloads what it needs. In Chrome or Edge, press <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>Delete</kbd>, choose <b>Cached images and files</b>, and leave cookies unticked so you stay logged in.</p>

<h2>6. Clear the GPU shader cache</h2>
<p>NVIDIA and AMD drivers keep pre-built game shaders that can quietly grow to several gigabytes. It's safe to clear; the only side effect is a bit of stutter the first time you launch each game afterwards. <a href="../clear-shader-cache/">How to clear the shader cache</a>.</p>

<h2>7. Remove the old Windows backup</h2>
<p>After a big Windows update, your previous version is kept in a <code>Windows.old</code> folder that can be 15–30 GB. If your PC has been working fine, you can remove it. <a href="../delete-windows-old/">How to delete Windows.old safely</a>.</p>

<h2>8. Find out what's actually using the space</h2>
<p>If you've done all of the above and the drive is still full, something specific is eating it: a game, a video folder, a virtual machine, Docker, or a runaway log file. <a href="../whats-taking-up-space/">How to see what's taking up space</a>.</p>

<h2>What you should never delete by hand</h2>
<ul>
  <li><code>C:\\Windows</code>, especially <code>WinSxS</code> and <code>System32</code>. Use the cleanup tools above instead.</li>
  <li><code>pagefile.sys</code> and <code>hiberfil.sys</code>. Windows manages these.</li>
  <li>Anything inside <code>Program Files</code>. Uninstall apps properly instead.</li>
  <li>Folders in <code>AppData</code> you don't recognise. Caches are fine; app data and settings are not.</li>
</ul>
<div class="callout"><p><b>Tip:</b> Try to keep at least 10–15% of your drive free. Windows needs room for updates and virtual memory, and SSDs slow down when they're nearly full.</p></div>
`,
  },
  {
    slug: "delete-temp-files",
    title: "How to delete temporary files on Windows (and what's safe to remove)",
    nav: "How to delete temporary files safely",
    description: "Step-by-step: how to delete temporary files on Windows 11 and 10, where the Temp folders are, why some files won't delete, and what's safe to remove.",
    minutes: 4,
    tldr: "Press <kbd>Win</kbd> + <kbd>R</kbd>, type <code>%temp%</code>, select everything and delete it. Skip anything that says it's in use. It's safe: apps recreate what they need.",
    body: `
<p>Apps create temporary files while they work: installers unpack into them, browsers stash downloads, and updaters leave copies behind. Most apps never clean up after themselves, so the Temp folder slowly grows to gigabytes.</p>

<h2>Is it safe to delete temp files?</h2>
<p>Yes. Temp files are, by definition, things apps don't need to keep. The only files you can't delete are ones currently in use, and Windows will simply refuse to delete those. Nothing breaks.</p>

<h2>Method 1: the Temp folder (fastest)</h2>
<ol>
  <li>Press <kbd>Win</kbd> + <kbd>R</kbd>, type <code>%temp%</code> and press <kbd>Enter</kbd>.</li>
  <li>Press <kbd>Ctrl</kbd> + <kbd>A</kbd> to select everything, then <kbd>Shift</kbd> + <kbd>Delete</kbd> to delete permanently.</li>
  <li>When Windows says a file is in use, tick <b>Do this for all current items</b> and click <b>Skip</b>.</li>
</ol>
<p>Closing your other apps first lets you delete more.</p>

<h2>Method 2: Windows' own cleanup</h2>
<p>Open <b>Settings → System → Storage → Temporary files</b>, tick <b>Temporary files</b> and click <b>Remove files</b>. This also lets you clear other safe categories like Windows Update leftovers in the same pass.</p>

<h2>Method 3: the system Temp folder</h2>
<p>Windows has a second Temp folder for system processes at <code>C:\\Windows\\Temp</code>. You need admin rights to open it. It's safe to empty the same way, skipping files in use.</p>

<h2>What not to confuse with temp files</h2>
<ul>
  <li><b>Prefetch</b> (<code>C:\\Windows\\Prefetch</code>): small, and Windows uses it to start apps faster. Leave it.</li>
  <li><b>AppData folders in general</b>: only caches and Temp are safe; other folders hold your app settings.</li>
</ul>
<div class="callout"><p>Roomy only deletes temp files that are <b>older than a day</b>, so it never pulls the rug out from under an installer that's still running.</p></div>
`,
  },
  {
    slug: "clear-shader-cache",
    title: "How to clear the NVIDIA, AMD and DirectX shader cache (and is it safe?)",
    nav: "How to clear the shader cache (NVIDIA, AMD, DirectX)",
    description: "What the shader cache is, whether it's safe to delete, and how to clear the NVIDIA, AMD, DirectX and Steam shader caches on Windows to free up gigabytes.",
    minutes: 4,
    tldr: "It's safe to clear. Games rebuild their shaders automatically; you may see some stutter the first time you launch each game afterwards. Clearing it can free several GB and fix some graphics glitches after driver updates.",
    body: `
<p>Every time a game runs, your graphics driver compiles <b>shaders</b>, the small programs that draw lighting, shadows and effects, and saves them so the next launch is faster. That saved collection is the shader cache, and it can grow to several gigabytes, sometimes more than 10 GB with lots of games installed.</p>

<h2>Is it safe to delete the shader cache?</h2>
<p>Yes. Nothing is lost permanently. The trade-off is that each game rebuilds its shaders the next time you play, which can mean longer loading or a little stutter for the first few minutes. Clearing it is also a common fix for flickering or crashes after a driver update.</p>

<h2>DirectX shader cache (all GPUs)</h2>
<p>Open <b>Settings → System → Storage → Temporary files</b>, tick <b>DirectX Shader Cache</b> and click <b>Remove files</b>. The files live in <code>%LOCALAPPDATA%\\D3DSCache</code>.</p>

<h2>NVIDIA shader cache</h2>
<ol>
  <li>Close all games.</li>
  <li>Press <kbd>Win</kbd> + <kbd>R</kbd>, type <code>%LOCALAPPDATA%\\NVIDIA</code> and press <kbd>Enter</kbd>.</li>
  <li>Open <code>DXCache</code> and <code>GLCache</code> and delete what's inside them. Skip files in use.</li>
</ol>
<p>You can also cap its size in <b>NVIDIA Control Panel → Manage 3D settings → Shader Cache Size</b>.</p>

<h2>AMD shader cache</h2>
<p>Open <b>AMD Software: Adrenalin Edition → Gaming → Graphics</b>, open <b>Advanced</b>, and click <b>Perform Reset</b> next to <b>Reset Shader Cache</b>. The files are in <code>%LOCALAPPDATA%\\AMD\\DxCache</code> and <code>DxcCache</code>.</p>

<h2>Steam's shader pre-caching</h2>
<p>Steam keeps its own per-game shader cache in <code>steamapps\\shadercache</code> inside each Steam library folder. It's safe to delete; Steam re-downloads or rebuilds it. You can turn the feature off under <b>Steam → Settings → Downloads → Shader Pre-Caching</b>, but it's usually worth keeping on.</p>
`,
  },
  {
    slug: "delete-windows-old",
    title: "Can I delete the Windows.old folder? How to remove it safely",
    nav: "Can I delete Windows.old?",
    description: "Windows.old is a backup of your previous Windows version that can take 15–30 GB. Here's when it's safe to delete and the right way to remove it on Windows 11 and 10.",
    minutes: 3,
    tldr: "Yes, if your PC has been working fine since the last big update. Remove it with <b>Settings → System → Storage → Temporary files → Previous Windows installation(s)</b>. Don't delete it in File Explorer.",
    body: `
<p>When Windows installs a major update (like a new Windows 11 version) or you upgrade from Windows 10, it keeps your old system in <code>C:\\Windows.old</code>. That's what lets you roll back if something goes wrong, and it's often <b>15–30 GB</b>.</p>

<h2>Is it safe to delete Windows.old?</h2>
<p>Yes, as long as you're happy with the current version. Once it's gone you can't use <b>Go back</b> in Recovery settings. Windows deletes it by itself after about 10 days anyway, but you can do it sooner if you need the space now.</p>
<div class="callout"><p>Before deleting, check that none of your personal files ended up only in <code>Windows.old\\Users</code>. That's rare, but it happens after some upgrades.</p></div>

<h2>The right way to delete it (Windows 11 and 10)</h2>
<ol>
  <li>Open <b>Settings → System → Storage</b>.</li>
  <li>Click <b>Temporary files</b>.</li>
  <li>Tick <b>Previous Windows installation(s)</b> and click <b>Remove files</b>.</li>
</ol>
<p>On Windows 10 you can also use <b>Disk Cleanup → Clean up system files → Previous Windows installation(s)</b>.</p>

<h2>Why you can't just delete it in File Explorer</h2>
<p>The folder is owned by the system, so Explorer will ask for permissions over and over and usually fail halfway, leaving a broken half-deleted folder. The Settings method removes it properly.</p>
`,
  },
  {
    slug: "windows-update-files",
    title: "Is it safe to delete Windows Update files? (SoftwareDistribution explained)",
    nav: "Is it safe to delete Windows Update files?",
    description: "Windows keeps old update files in SoftwareDistribution and the Delivery Optimization cache. Here's what's safe to delete, how to do it properly, and what to leave alone.",
    minutes: 4,
    tldr: "Yes. Use <b>Settings → System → Storage → Temporary files</b> and tick <b>Windows Update Cleanup</b> and <b>Delivery Optimization Files</b>. If you want to clear the download folder by hand, stop the Windows Update service first and only empty <code>SoftwareDistribution\\Download</code>.",
    body: `
<p>Windows Update downloads every update to your PC before installing it, and keeps several kinds of leftovers afterwards. Together they can easily take 5–15 GB.</p>

<h2>The three kinds of update leftovers</h2>
<ul>
  <li><b>Downloaded update files</b> in <code>C:\\Windows\\SoftwareDistribution\\Download</code>. Once updates are installed, these are just copies.</li>
  <li><b>Delivery Optimization files</b>: updates your PC keeps so it can share them with other PCs. Safe to delete.</li>
  <li><b>Superseded components</b> in <code>C:\\Windows\\WinSxS</code>: older versions of system files replaced by updates. <b>Never delete these by hand</b>; use the tools below.</li>
</ul>

<h2>The easy, safe way</h2>
<ol>
  <li>Open <b>Settings → System → Storage → Temporary files</b>.</li>
  <li>Tick <b>Windows Update Cleanup</b> and <b>Delivery Optimization Files</b>.</li>
  <li>Click <b>Remove files</b>.</li>
</ol>

<h2>Clearing SoftwareDistribution\\Download by hand</h2>
<p>This is also the classic fix when Windows Update is stuck. Open <b>Terminal (Admin)</b> or Command Prompt as administrator and run:</p>
<pre><code>net stop wuauserv
net stop bits</code></pre>
<p>Then delete everything inside <code>C:\\Windows\\SoftwareDistribution\\Download</code> (not the folder itself), and start the services again:</p>
<pre><code>net start wuauserv
net start bits</code></pre>
<div class="callout"><p>Don't delete the whole <code>SoftwareDistribution</code> folder unless you're troubleshooting. It also holds your update history, which gets reset.</p></div>

<h2>Shrinking WinSxS properly</h2>
<p>If <code>WinSxS</code> is huge, ask Windows to clean up superseded components itself:</p>
<pre><code>DISM /Online /Cleanup-Image /StartComponentCleanup</code></pre>
<p>It can take a while, and you won't be able to uninstall updates that were already installed.</p>
`,
  },
  {
    slug: "whats-taking-up-space",
    title: "How to see what's taking up space on your PC (Windows 11 and 10)",
    nav: "How to see what's taking up space on your PC",
    description: "Find out exactly what's using your disk space on Windows: the built-in Storage page, hidden space hogs like hiberfil.sys, WinSxS, restore points and WSL, and how to find the biggest folders.",
    minutes: 5,
    tldr: "Start with <b>Settings → System → Storage</b> for a category breakdown. For folder-by-folder detail, use a disk space map tool like Roomy, WinDirStat or WizTree. Don't forget hidden hogs: hibernation, restore points and virtual disks.",
    body: `
<p>Windows' "Other" and "System & reserved" categories are famously unhelpful. Here's how to see where your space actually went.</p>

<h2>1. The built-in Storage page</h2>
<p>Open <b>Settings → System → Storage</b>. Windows shows a breakdown by category; click <b>Show more categories</b> to see everything. Clicking a category, like <b>Apps &amp; features</b> or <b>Other</b>, takes you to the details.</p>

<h2>2. A folder-by-folder map</h2>
<p>File Explorer can't sort folders by size, so a disk map tool is the fastest way to find a 40 GB surprise. Roomy has a built-in <b>Space map</b>: pick a drive, then click down through folders to see what's biggest, plus a list of the largest single files on your PC. WinDirStat and WizTree are other good free options.</p>

<h2>3. Hidden space hogs to check</h2>
<h3>Hibernation file (hiberfil.sys)</h3>
<p>Usually 40% of your RAM. If you never use Hibernate, you can turn it off from an admin terminal with <code>powercfg /h off</code>. This also disables Fast Startup.</p>
<h3>System Restore points</h3>
<p>Search for <b>Create a restore point</b>, select your C: drive, click <b>Configure</b>, and lower the max usage or delete old points.</p>
<h3>WinSxS (component store)</h3>
<p>It looks huge, but much of it is shared with <code>System32</code>. Never delete it by hand; run <code>DISM /Online /Cleanup-Image /StartComponentCleanup</code> instead.</p>
<h3>WSL and Docker virtual disks</h3>
<p>If you use WSL or Docker Desktop, their <code>.vhdx</code> files in <code>AppData\\Local</code> grow and never shrink by themselves. Prune unused images with <code>docker system prune</code>, then compact the disk.</p>
<h3>Game libraries</h3>
<p>Steam, Epic and Xbox games often live under <code>Program Files (x86)</code> or <code>XboxGames</code> and are the largest folders on most gaming PCs.</p>
`,
  },
];
