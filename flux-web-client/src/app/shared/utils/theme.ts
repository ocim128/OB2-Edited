import { getBaseUrl } from './host';

export type ThemeMode = 'light' | 'dark' | 'auto';

/**
 * Gets the current theme mode from localStorage or returns 'auto' as default
 */
export function getThemeMode(): ThemeMode {
  const stored = localStorage.getItem('theme-mode');
  if (stored === 'light' || stored === 'dark' || stored === 'auto') {
    return stored;
  }
  return 'auto';
}

/**
 * Sets the theme mode and applies the corresponding CSS class to the document root
 * @param mode - 'light', 'dark', or 'auto' (follows system preference)
 */
export function setThemeMode(mode: ThemeMode): void {
  localStorage.setItem('theme-mode', mode);
  applyThemeMode(mode);
}

/**
 * Applies the theme mode CSS class to the document root
 */
function applyThemeMode(mode: ThemeMode): void {
  const root = document.documentElement;
  
  // Remove existing theme classes
  root.classList.remove('light-theme', 'dark-theme');
  
  if (mode === 'light') {
    root.classList.add('light-theme');
  } else if (mode === 'dark') {
    root.classList.add('dark-theme');
  }
  // 'auto' mode: no class added, lets CSS @media query handle it
}

/**
 * Applies the backend theme CSS file
 */
export function applyAppTheme(name: string | null = null): void {
  let path = `${getBaseUrl()}/settings/theme`;

  if (name !== null) {
    path += `?name=${encodeURIComponent(name)}`;
  }

  const theme = document.getElementById('app-theme');

  if (theme) {
    theme.setAttribute('href', path);
  }
  
  // Also apply the current theme mode
  const mode = getThemeMode();
  applyThemeMode(mode);
}

