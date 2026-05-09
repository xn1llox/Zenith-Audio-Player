# NVIDIA NIM request example
#
# Do not commit real API keys. Set the key in your shell before running:
#   $env:NVIDIA_API_KEY='your-api-key'    # PowerShell
#   export NVIDIA_API_KEY='your-api-key'  # bash

payload=$(cat <<'JSON'
{
  "model": "google/gemma-4-31b-it",
  "messages": [{"role":"user","content":""}],
  "temperature": 1,
  "top_p": 0.95,
  "max_tokens": 480,
  "stream": true
}
JSON
)

curl -sS -N \
  --request POST \
  --url "https://integrate.api.nvidia.com/v1/chat/completions" \
  --header "Authorization: Bearer ${NVIDIA_API_KEY}" \
  --header "Content-Type: application/json" \
  --data "$payload"
