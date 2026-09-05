# SkillForge Logo Background Component

## Overview

The Logo Background is a performance-optimized, dynamic watermark-style background featuring company logos that float subtly across the SkillForge portal. It enhances the visual appeal while maintaining professional aesthetics and excellent performance.

## Features

### 🎨 Design
- **Low-Opacity Watermarks**: Logos displayed at 6% opacity (configurable: 4-8%)
- **Subtle Floating Animation**: Logos drift smoothly using sine and cosine wave patterns
- **Gradient Base**: Dark theme with linear gradient background (customizable)
- **Professional Branding**: Features 20 major tech companies

### ⚡ Performance
- **Lazy Loading**: Logos loaded on-demand via Clearbit Logo API
- **IntersectionObserver**: Pauses animation when tab is not visible
- **RequestAnimationFrame**: Smooth 60fps animation without jank
- **Max 25 Logos**: Limits DOM elements rendered at once
- **CSS Optimization**: Uses `will-change` and `transform` for GPU acceleration

### 🔄 Fallback Support
- **Clearbit Logo API**: Primary logo source with 2s timeout
- **Monogram Fallback**: Styled letter badges if logo fails to load
- **Responsive Design**: Adapts opacity for mobile/landscape
- **Accessibility**: Respects `prefers-reduced-motion` for users who prefer static displays

## Companies Included

### Global Tech Giants
- Google, Microsoft, Amazon, Meta, Apple, Netflix

### Indian Startups
- Razorpay, Zepto, Swiggy, Zomato, Flipkart, PhonePe, CRED, Freshworks, Zoho

### Developer Tools
- Postman, BrowserStack, Chargebee, Stripe, GitHub, Figma

## Usage

### Basic Initialization

```typescript
import { initializeLogoBackground } from './logo-background';

// Initialize on page load
const logoBackground = initializeLogoBackground();

// Pause animation (optional)
logoBackground?.pause();

// Resume animation
logoBackground?.resume();

// Cleanup on page unload
logoBackground?.destroy();
```

### HTML Setup

```html
<!DOCTYPE html>
<html>
<head>
    <!-- Include CSS -->
    <link rel="stylesheet" href="./logo-background.css">
</head>
<body>
    <!-- Logo Background Container (must be first) -->
    <div id="logo-background"></div>

    <!-- Your content here -->
    <header class="header">...</header>
    
    <!-- Include TypeScript -->
    <script type="module" src="./main.ts"></script>
</body>
</html>
```

## Configuration

### Adjust Opacity

Edit `logo-background.ts` in the constructor:
```typescript
private readonly logoOpacity = 0.06; // Change to 0.04-0.10 for different opacity
```

### Change Animation Duration

Modify the animation durations array:
```typescript
private readonly animationDurations = [8, 10, 12, 14, 16]; // seconds
```

### Adjust Drift Distance

In the `LogoTile.update()` method:
```typescript
this.offsetX = Math.sin(progress * Math.PI * 2) * 15; // Horizontal drift (pixels)
this.offsetY = Math.cos(progress * Math.PI * 2) * 10; // Vertical drift (pixels)
```

### Change Max Logos

Modify:
```typescript
private readonly maxLogosOnScreen = 25; // Adjust based on device capability
```

## CSS Classes

### Logo Tiles
```css
.logo-tile {
    /* Controlled by component */
    opacity: 0.06;
    position: absolute;
    will-change: transform;
}
```

### Blur Effect
```css
#logo-background.blurred .logo-tile {
    filter: blur(3px);
}
```

## Responsive Behavior

### Mobile (max-width: 768px)
- Reduced opacity to 4% (less visual distraction)
- Disabled background pulse animation
- Smaller logo size for touch interfaces

### Landscape (max-height: 600px)
- Further reduced opacity to 3% (limited vertical space)
- Optimized for narrow viewport

### Dark/Light Mode
- Automatic filter adjustments based on `prefers-color-scheme`
- Darker on light backgrounds, brighter on dark

### Reduced Motion
- Completely disabled animations if `prefers-reduced-motion: reduce` is set
- Static background for accessibility

## Performance Metrics

| Metric | Value |
|--------|-------|
| Max DOM Elements | 25 |
| Logo Load Timeout | 2 seconds |
| Animation FPS | 60 (target) |
| Opacity Range | 3-8% |
| CSS Animations | GPU-accelerated |
| JS Execution | <16ms per frame |

## Browser Support

- ✅ Chrome 90+
- ✅ Firefox 88+
- ✅ Safari 14+
- ✅ Edge 90+
- ✅ Mobile browsers (iOS Safari, Chrome Android)

## API Reference

### CompanyLogoBackground

#### Constructor
```typescript
constructor(containerId: string)
```

#### Methods
```typescript
pause(): void          // Pause animation (e.g., when tab loses focus)
resume(): void         // Resume animation
destroy(): void        // Clean up and remove component
```

#### Events
- **Visibility Change**: Automatically pauses when tab is inactive
- **Intersection Observer**: Triggers on visibility change

### LogoTile

Internal class managing individual logo animation and loading.

#### Properties
```typescript
element: HTMLElement   // DOM element for the logo
```

#### Methods
```typescript
update(now: number): void  // Update position based on time (called every frame)
```

## Customization Examples

### Add Custom Companies

Edit the `companies` array in `logo-background.ts`:
```typescript
private companies = [
    { name: 'Your Company', domain: 'yourcompany.com' },
    // ... more companies
];
```

### Change Logo Size

Modify the grid creation:
```typescript
const tileSize = 150; // Increase from 120 for larger logos
```

### Customize Animation Pattern

Override the `update()` method in `LogoTile`:
```typescript
// Circular rotation instead of drift
const angle = (progress * Math.PI * 2);
const radius = 20;
this.offsetX = Math.cos(angle) * radius;
this.offsetY = Math.sin(angle) * radius;
```

## Troubleshooting

### Logos Not Appearing
1. Verify Clearbit API is accessible: https://logo.clearbit.com/google.com
2. Check browser console for errors
3. Ensure CSS file is loaded: `logo-background.css`

### Animation Stuttering
1. Reduce `maxLogosOnScreen` count
2. Disable blur effects
3. Check browser DevTools Performance tab

### High CPU Usage
1. Enable `prefers-reduced-motion` setting
2. Reduce `maxLogosOnScreen`
3. Increase animation duration

### Logos Not Loading in Production
- Add CORS headers for Clearbit API if behind proxy
- Use fallback monogram mode (automatic if Clearbit fails)

## Future Enhancements

- [ ] Configurable company list via API endpoint
- [ ] Logo caching in IndexedDB for offline support
- [ ] Mouse-following logo interaction
- [ ] Tap-to-explore company details on mobile
- [ ] Theme customization (light/dark/custom colors)
- [ ] Performance metrics dashboard

## Files

```
src/
├── logo-background.ts      # Main component class
├── logo-background.css     # Styling and animations
├── main.ts                 # Application entry point
├── index.html             # HTML template
└── styles.css             # Global styles
```

## License

Part of SkillForge Platform - See main LICENSE file
