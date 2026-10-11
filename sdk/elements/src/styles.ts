/**
 * The components' look: the page's font and colors, with an accent and a radius that `--kimlik-color-primary`,
 * `--kimlik-color-danger` and `--kimlik-radius` change.
 */
export const styles = `
:host {
  display: block;
  font: inherit;
  color: inherit;
  --primary: var(--kimlik-color-primary, #4f46e5);
  --danger: var(--kimlik-color-danger, #b91c1c);
  --radius: var(--kimlik-radius, 6px);
  --muted: var(--kimlik-color-muted, color-mix(in srgb, currentColor 60%, transparent));
  --border: var(--kimlik-color-border, color-mix(in srgb, currentColor 18%, transparent));
}
:host([hidden]) { display: none; }
* { box-sizing: border-box; }
section + section { margin-top: 1.5rem; }
h2 { font-size: 1.125rem; margin: 0 0 0.75rem; }
p { margin: 0 0 0.75rem; }
ul { list-style: none; margin: 0; padding: 0; }
li { padding: 0.75rem 0; border-top: 1px solid var(--border); }
li:first-child { border-top: none; }
.muted, small { color: var(--muted); }
.row { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: center; justify-content: space-between; }
.actions { display: flex; flex-wrap: wrap; gap: 0.5rem; }
form { display: grid; gap: 0.75rem; max-width: 32rem; }
label { display: grid; gap: 0.25rem; font-weight: 500; }
fieldset { border: 1px solid var(--border); border-radius: var(--radius); padding: 0.5rem 0.75rem; margin: 0; }
fieldset label, label.check { display: flex; gap: 0.5rem; align-items: center; font-weight: 400; }
input[type="text"], input[type="email"], input[type="url"] {
  font: inherit; color: inherit; background: transparent;
  padding: 0.5rem 0.625rem; border: 1px solid var(--border); border-radius: var(--radius);
}
button {
  font: inherit; cursor: pointer; padding: 0.5rem 0.875rem; border-radius: var(--radius);
  border: 1px solid var(--primary); background: var(--primary); color: #fff;
}
button.secondary { background: transparent; color: var(--primary); }
button.danger { border-color: var(--danger); background: var(--danger); color: #fff; }
button.link { border: none; background: none; color: inherit; padding: 0; text-align: start; width: 100%; }
button:disabled { opacity: 0.5; cursor: not-allowed; }
button:focus-visible, input:focus-visible { outline: 2px solid var(--primary); outline-offset: 2px; }
[role="alert"] { color: var(--danger); }
.visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
`;
