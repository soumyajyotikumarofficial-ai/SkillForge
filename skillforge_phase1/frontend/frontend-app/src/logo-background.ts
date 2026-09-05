/**
 * Company Logo Background - Dynamic watermark-style floating logo tiles
 * 
 * Features:
 * - Low-opacity watermark logos (0.04-0.08 opacity)
 * - Subtle floating animation (5-15s duration)
 * - Lazy loading with IntersectionObserver
 * - Clearbit Logo API integration
 * - Monogram fallback for missing logos
 * - Performance optimized: max 25 logos rendered at once
 * - Uses requestAnimationFrame for smooth animation
 */

export class CompanyLogoBackground {
    private container: HTMLElement;
    private logoGrid: LogoTile[] = [];
    private observer: IntersectionObserver | null = null;
    private animationFrameId: number | null = null;
    private isVisible: boolean = true;
    private readonly maxLogosOnScreen = 25;
    private readonly logoOpacity = 0.06; // 6% opacity for subtle watermark effect
    private readonly animationDurations = [8, 10, 12, 14, 16]; // seconds

    // Company data with domains for Clearbit API
    private companies = [
        { name: 'Google', domain: 'google.com' },
        { name: 'Microsoft', domain: 'microsoft.com' },
        { name: 'Amazon', domain: 'amazon.com' },
        { name: 'Meta', domain: 'meta.com' },
        { name: 'Apple', domain: 'apple.com' },
        { name: 'Netflix', domain: 'netflix.com' },
        { name: 'Razorpay', domain: 'razorpay.com' },
        { name: 'Flipkart', domain: 'flipkart.com' },
        { name: 'Swiggy', domain: 'swiggy.com' },
        { name: 'Zomato', domain: 'zomato.com' },
        { name: 'PhonePe', domain: 'phonepe.com' },
        { name: 'CRED', domain: 'cred.club' },
        { name: 'Freshworks', domain: 'freshworks.com' },
        { name: 'Zoho', domain: 'zoho.com' },
        { name: 'Postman', domain: 'postman.com' },
        { name: 'BrowserStack', domain: 'browserstack.com' },
        { name: 'Chargebee', domain: 'chargebee.com' },
        { name: 'Stripe', domain: 'stripe.com' },
        { name: 'GitHub', domain: 'github.com' },
        { name: 'Figma', domain: 'figma.com' }
    ];

    constructor(containerId: string) {
        const element = document.getElementById(containerId);
        if (!element) {
            console.error(`Container ${containerId} not found`);
            return;
        }
        this.container = element;
        this.init();
    }

    /**
     * Initialize the logo background
     */
    private init(): void {
        // Set up container styles
        this.container.style.position = 'fixed';
        this.container.style.top = '0';
        this.container.style.left = '0';
        this.container.style.width = '100%';
        this.container.style.height = '100%';
        this.container.style.zIndex = '-1';
        this.container.style.pointerEvents = 'none';
        this.container.style.overflow = 'hidden';

        // Create grid of logos
        this.createLogoGrid();

        // Set up IntersectionObserver for performance
        this.setupIntersectionObserver();

        // Set up visibility change listener
        document.addEventListener('visibilitychange', this.handleVisibilityChange.bind(this));

        // Start animation
        this.animate();
    }

    /**
     * Create a grid of logo tiles
     */
    private createLogoGrid(): void {
        const tileSize = 120; // pixels
        const cols = Math.ceil(window.innerWidth / tileSize) + 2;
        const rows = Math.ceil(window.innerHeight / tileSize) + 2;

        for (let row = 0; row < rows; row++) {
            for (let col = 0; col < cols; col++) {
                // Limit total logos on screen
                if (this.logoGrid.length >= this.maxLogosOnScreen) break;

                const company = this.companies[
                    (row * cols + col) % this.companies.length
                ];

                const tile = new LogoTile(
                    company.name,
                    company.domain,
                    col * tileSize,
                    row * tileSize,
                    tileSize,
                    this.logoOpacity,
                    this.animationDurations[
                        (row * cols + col) % this.animationDurations.length
                    ]
                );

                this.container.appendChild(tile.element);
                this.logoGrid.push(tile);
            }
        }
    }

    /**
     * Set up IntersectionObserver to pause animation when tab not visible
     */
    private setupIntersectionObserver(): void {
        this.observer = new IntersectionObserver(
            (entries) => {
                entries.forEach((entry) => {
                    if (entry.target === this.container) {
                        this.isVisible = entry.isIntersecting;
                    }
                });
            },
            { threshold: 0.1 }
        );

        this.observer.observe(this.container);
    }

    /**
     * Handle visibility change (tab focus/blur)
     */
    private handleVisibilityChange(): void {
        this.isVisible = !document.hidden;
    }

    /**
     * Animate logos with requestAnimationFrame for smooth performance
     */
    private animate(): void {
        if (!this.isVisible) {
            this.animationFrameId = requestAnimationFrame(() => this.animate());
            return;
        }

        const now = Date.now();

        this.logoGrid.forEach((tile) => {
            tile.update(now);
        });

        this.animationFrameId = requestAnimationFrame(() => this.animate());
    }

    /**
     * Destroy the background and clean up
     */
    public destroy(): void {
        if (this.animationFrameId !== null) {
            cancelAnimationFrame(this.animationFrameId);
        }

        if (this.observer) {
            this.observer.disconnect();
        }

        this.logoGrid.forEach((tile) => {
            if (tile.element.parentNode) {
                tile.element.parentNode.removeChild(tile.element);
            }
        });

        this.logoGrid = [];
    }

    /**
     * Pause animation
     */
    public pause(): void {
        this.isVisible = false;
    }

    /**
     * Resume animation
     */
    public resume(): void {
        this.isVisible = true;
    }
}

/**
 * Individual logo tile with animation
 */
class LogoTile {
    element: HTMLElement;
    private startX: number;
    private startY: number;
    private offsetX: number = 0;
    private offsetY: number = 0;
    private startTime: number = Date.now();
    private duration: number; // ms

    constructor(
        companyName: string,
        domain: string,
        x: number,
        y: number,
        size: number,
        opacity: number,
        durationSeconds: number
    ) {
        this.startX = x;
        this.startY = y;
        this.duration = durationSeconds * 1000;

        this.element = document.createElement('div');
        this.element.className = 'logo-tile';
        this.element.style.position = 'absolute';
        this.element.style.width = `${size}px`;
        this.element.style.height = `${size}px`;
        this.element.style.opacity = String(opacity);
        this.element.style.display = 'flex';
        this.element.style.alignItems = 'center';
        this.element.style.justifyContent = 'center';
        this.element.style.filter = 'blur(0px)';
        this.element.style.transition = 'filter 0.3s ease';

        // Try to load from Clearbit, fallback to monogram
        this.loadLogo(companyName, domain, size, opacity);
    }

    /**
     * Load logo from Clearbit API or create monogram fallback
     */
    private async loadLogo(
        companyName: string,
        domain: string,
        size: number,
        opacity: number
    ): Promise<void> {
        const logoClearbitUrl = `https://logo.clearbit.com/${domain}?size=${size}`;

        const img = document.createElement('img');
        img.style.width = '80%';
        img.style.height = '80%';
        img.style.objectFit = 'contain';
        img.style.pointerEvents = 'none';

        // Try Clearbit first
        let loadSuccess = false;
        await new Promise<void>((resolve) => {
            const timeout = setTimeout(() => {
                resolve();
            }, 2000); // 2s timeout for Clearbit

            img.onload = () => {
                clearTimeout(timeout);
                loadSuccess = true;
                this.element.appendChild(img);
                resolve();
            };

            img.onerror = () => {
                clearTimeout(timeout);
                resolve();
            };

            img.src = logoClearbitUrl;
        });

        // Fallback to monogram if logo failed to load
        if (!loadSuccess) {
            this.createMonogram(companyName, size, opacity);
        }
    }

    /**
     * Create a styled monogram fallback
     */
    private createMonogram(companyName: string, size: number, opacity: number): void {
        const monogram = document.createElement('div');
        monogram.textContent = companyName.charAt(0).toUpperCase();
        monogram.style.fontSize = `${size * 0.4}px`;
        monogram.style.fontWeight = '700';
        monogram.style.color = '#6366f1';
        monogram.style.border = '2px solid #6366f1';
        monogram.style.borderRadius = '8px';
        monogram.style.padding = '8px';
        monogram.style.width = '60%';
        monogram.style.height = '60%';
        monogram.style.display = 'flex';
        monogram.style.alignItems = 'center';
        monogram.style.justifyContent = 'center';

        this.element.appendChild(monogram);
    }

    /**
     * Update tile position for floating animation
     */
    public update(now: number): void {
        const elapsed = now - this.startTime;
        const progress = (elapsed % this.duration) / this.duration;

        // Subtle floating animation using sine waves
        // Moves in a smooth, continuous pattern
        this.offsetX = Math.sin(progress * Math.PI * 2) * 15; // 15px horizontal drift
        this.offsetY = Math.cos(progress * Math.PI * 2) * 10; // 10px vertical drift

        this.element.style.transform = `translate(${this.startX + this.offsetX}px, ${this.startY + this.offsetY}px)`;

        // Optional: Add subtle blur effect on hover area
        // This would require tracking mouse position
    }
}

/**
 * Initialize logo background on page load
 */
export function initializeLogoBackground(): CompanyLogoBackground | null {
    const container = document.getElementById('logo-background');
    if (!container) {
        console.warn('Logo background container not found');
        return null;
    }

    return new CompanyLogoBackground('logo-background');
}
