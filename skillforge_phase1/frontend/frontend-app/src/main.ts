const app = document.getElementById('app')
const status = document.createElement('div')
status.innerText = 'Loading...'
if (app) app.appendChild(status)

fetch('http://localhost:5000/api/ai/ping')
	.then(r => r.json())
	.then(j => { status.innerText = 'Backend: ' + (j.message ?? JSON.stringify(j)) })
	.catch(e => { status.innerText = 'Fetch error: ' + (e?.message ?? e) })

console.log('SkillForge frontend placeholder started')
