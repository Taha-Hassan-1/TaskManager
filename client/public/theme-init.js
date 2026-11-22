// Runs before the page paints, so a saved light or dark choice never flashes the other theme first.
// (A file of its own, because the Content-Security-Policy allows no inline scripts.) Keep the key in step with core/theme.ts.
try {
  var saved = localStorage.getItem('teamboard-theme');
  if (saved === 'light' || saved === 'dark') document.documentElement.setAttribute('data-theme', saved);
} catch (e) {
  /* storage blocked: the system preference applies */
}
