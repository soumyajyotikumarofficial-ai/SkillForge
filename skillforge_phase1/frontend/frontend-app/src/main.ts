import './styles.css';
import './logo-background.css';
import { initializeLogoBackground } from './logo-background';

/**
 * SkillForge Frontend Application Entry Point
 * Initializes logo background and resume upload functionality
 */

// Initialize logo background on page load
let logoBackground: ReturnType<typeof initializeLogoBackground> = null;

document.addEventListener('DOMContentLoaded', () => {
    // Initialize company logo background
    logoBackground = initializeLogoBackground();
    
    // Initialize upload form
    initializeUploadForm();
    
    // Initialize navigation
    initializeNavigation();
});

/**
 * Initialize resume upload form
 */
function initializeUploadForm(): void {
    const uploadForm = document.getElementById('uploadForm') as HTMLFormElement;
    const fileInput = document.getElementById('fileInput') as HTMLInputElement;
    const dropZone = document.getElementById('dropZone') as HTMLDivElement;

    if (!uploadForm || !fileInput || !dropZone) {
        console.error('Upload form elements not found');
        return;
    }

    // Drag and drop handlers
    dropZone.addEventListener('dragover', (e) => {
        e.preventDefault();
        dropZone.style.borderColor = '#6366f1';
        dropZone.style.background = 'rgba(99, 102, 241, 0.15)';
    });

    dropZone.addEventListener('dragleave', () => {
        dropZone.style.borderColor = 'rgba(99, 102, 241, 0.5)';
        dropZone.style.background = 'rgba(99, 102, 241, 0.05)';
    });

    dropZone.addEventListener('drop', (e) => {
        e.preventDefault();
        const files = e.dataTransfer?.files;
        if (files?.length) {
            fileInput.files = files;
        }
    });

    // Click to browse
    dropZone.addEventListener('click', () => {
        fileInput.click();
    });

    // Form submission
    uploadForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        const file = fileInput.files?.[0];

        if (!file) {
            alert('Please select a file');
            return;
        }

        const validTypes = ['application/pdf', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'text/plain'];
        if (!validTypes.includes(file.type)) {
            alert('Please select a PDF, DOCX, or TXT file');
            return;
        }

        await submitResume(file);
    });
}

/**
 * Submit resume for analysis
 */
async function submitResume(file: File): Promise<void> {
    const submitBtn = document.querySelector('.submit-btn') as HTMLButtonElement;
    if (!submitBtn) return;

    submitBtn.disabled = true;
    submitBtn.textContent = 'Analysing...';

    try {
        const formData = new FormData();
        formData.append('file', file);

        // Replace with actual API endpoint
        const response = await fetch('/api/ai/analyse-resume', {
            method: 'POST',
            body: formData
        });

        if (!response.ok) {
            throw new Error(`API error: ${response.statusCode}`);
        }

        const result = await response.json();
        displayResults(result);
    } catch (error) {
        console.error('Error uploading resume:', error);
        alert('Error analysing resume. Please try again.');
    } finally {
        submitBtn.disabled = false;
        submitBtn.textContent = 'Analyse Resume';
    }
}

/**
 * Display analysis results
 */
function displayResults(result: any): void {
    const uploadSection = document.getElementById('uploadSection') as HTMLDivElement;
    const resultsSection = document.getElementById('resultsSection') as HTMLDivElement;

    if (!uploadSection || !resultsSection) return;

    // Populate results
    const scoreNumber = document.getElementById('scoreNumber');
    if (scoreNumber) scoreNumber.textContent = result.resumeScore || '0';

    const candidateName = document.getElementById('candidateName');
    if (candidateName) candidateName.textContent = result.name || 'Unknown';

    // Populate skills
    const skillsContainer = document.querySelector('.skills-grid');
    if (skillsContainer && result.skills) {
        skillsContainer.innerHTML = result.skills
            .map((skill: string) => `<div class="skill-chip">${skill}</div>`)
            .join('');
    }

    // Hide upload, show results
    uploadSection.style.display = 'none';
    resultsSection.style.display = 'block';
}

/**
 * Initialize navigation
 */
function initializeNavigation(): void {
    const backBtn = document.getElementById('backBtn') as HTMLButtonElement;
    const uploadSection = document.getElementById('uploadSection') as HTMLDivElement;
    const resultsSection = document.getElementById('resultsSection') as HTMLDivElement;

    if (!backBtn || !uploadSection || !resultsSection) return;

    backBtn.addEventListener('click', () => {
        resultsSection.style.display = 'none';
        uploadSection.style.display = 'block';
        
        // Reset form
        const fileInput = document.getElementById('fileInput') as HTMLInputElement;
        if (fileInput) fileInput.value = '';
    });
}

/**
 * Cleanup on page unload
 */
window.addEventListener('beforeunload', () => {
    if (logoBackground) {
        logoBackground.destroy();
    }
});

/**
 * Handle visibility changes (tab switch)
 */
document.addEventListener('visibilitychange', () => {
    if (!logoBackground) return;
    
    if (document.hidden) {
        logoBackground.pause();
    } else {
        logoBackground.resume();
    }
});

/**
 * Add blur effect to logo background on mouse enter/leave content
 */
document.addEventListener('DOMContentLoaded', () => {
    const contentArea = document.querySelector('.upload-section, .results-section');
    const logoBg = document.getElementById('logo-background');

    if (contentArea && logoBg) {
        contentArea.addEventListener('mouseenter', () => {
            logoBg.classList.add('blurred');
        });

        contentArea.addEventListener('mouseleave', () => {
            logoBg.classList.remove('blurred');
        });
    }
});
