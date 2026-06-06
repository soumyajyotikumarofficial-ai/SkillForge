const app = document.getElementById('app') as HTMLElement | null
const status = document.getElementById('status') as HTMLElement | null
const result = document.getElementById('result') as HTMLElement | null
const uploadForm = document.getElementById('uploadForm') as HTMLFormElement | null

if (status) status.innerText = 'Checking backend...'
fetch('http://localhost:5001/api/ai/ping')
	.then(r => r.json())
	.then(j => { if (status) status.innerText = 'Backend: ' + (j.message ?? JSON.stringify(j)) })
	.catch(e => { if (status) status.innerText = 'Backend fetch error' })

if (uploadForm) {
	uploadForm.addEventListener('submit', async (ev) => {
		ev.preventDefault()
		if (status) status.innerText = 'Uploading...'
		const input = document.getElementById('fileInput') as HTMLInputElement | null
		if (!input || !input.files || input.files.length === 0) { if (status) status.innerText = 'No file selected'; return }
		const fd = new FormData()
		fd.append('file', input.files[0])

		try {
			const res = await fetch('http://localhost:5001/api/candidate/upload', { method: 'POST', body: fd })
			const json = await res.json()
			if (status) status.innerText = 'Upload complete'
			if (result) result.innerText = JSON.stringify(json, null, 2)
		} catch (err) {
			if (status) status.innerText = 'Upload failed'
			if (result) result.innerText = String(err)
		}
	})
}

console.log('SkillForge frontend upload ready')
