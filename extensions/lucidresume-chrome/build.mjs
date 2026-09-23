import { build } from "esbuild";
import { cp, mkdir, rm } from "node:fs/promises";

await rm("dist", { recursive: true, force: true });
await mkdir("dist", { recursive: true });
await build({
  entryPoints: {
    background: "src/background.ts",
    content: "src/content.ts",
    sidepanel: "src/sidepanel.ts"
  },
  bundle: true,
  outdir: "dist",
  format: "iife",
  platform: "browser",
  target: "chrome138",
  sourcemap: true,
  minify: false
});
await cp("static/manifest.json", "dist/manifest.json");
await cp("static/sidepanel.html", "dist/sidepanel.html");
await cp("static/sidepanel.css", "dist/sidepanel.css");
