import { Injectable, signal, effect, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

@Injectable({
  providedIn: 'root'
})
export class AdminAuthService {
  private readonly router = inject(Router);
  private readonly STORAGE_KEY = 'stocklens_admin_active';
  private readonly SECRET_KEY = 'hetvi2026';

  isAdmin = signal<boolean>(false);
  isPinModalOpen = signal<boolean>(false);
  pinErrorMessage = signal<string>('');

  constructor() {
    // 1. Check existing session in localStorage
    const saved = localStorage.getItem(this.STORAGE_KEY);
    if (saved === 'true') {
      this.isAdmin.set(true);
    }

    // 2. Global listener for Ctrl + Shift + A shortcut
    if (typeof window !== 'undefined') {
      window.addEventListener('keydown', (event: KeyboardEvent) => {
        if ((event.ctrlKey || event.metaKey) && event.shiftKey && (event.key === 'A' || event.key === 'a')) {
          event.preventDefault();
          this.openPinPrompt();
        }
      });

      // 3. Check current URL query params on load (e.g. ?key=hetvi2026)
      const urlParams = new URLSearchParams(window.location.search);
      const key = urlParams.get('key');
      if (key && key.toLowerCase() === this.SECRET_KEY.toLowerCase()) {
        this.activateAdmin();
        // Clean URL query param without full page reload
        const cleanUrl = window.location.pathname;
        window.history.replaceState({}, document.title, cleanUrl);
      }
    }
  }

  openPinPrompt(): void {
    this.pinErrorMessage.set('');
    this.isPinModalOpen.set(true);
  }

  closePinPrompt(): void {
    this.isPinModalOpen.set(false);
    this.pinErrorMessage.set('');
  }

  submitPin(enteredPin: string): boolean {
    if (enteredPin && enteredPin.trim().toLowerCase() === this.SECRET_KEY.toLowerCase()) {
      this.activateAdmin();
      this.closePinPrompt();
      return true;
    } else {
      this.pinErrorMessage.set('Incorrect Admin Key. Access denied.');
      return false;
    }
  }

  activateAdmin(): void {
    this.isAdmin.set(true);
    localStorage.setItem(this.STORAGE_KEY, 'true');
  }

  logoutAdmin(): void {
    this.isAdmin.set(false);
    localStorage.removeItem(this.STORAGE_KEY);
  }
}
