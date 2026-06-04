# NVIDIA NIM request example
#
# Do not commit real API keys. Set the key in your shell before running:
#   $env:NVIDIA_API_KEY='your-api-key'    # PowerShell
#   export NVIDIA_API_KEY='your-api-key'  # bash

payload=$(cat <<'JSON'
{
  "model": "nvidia/nemotron-3-super-120b-a12b",
  "messages": [{"role":"user","content":""}],
  "temperature": 1,
  "top_p": 0.95,
  "max_tokens": 8192,
  "reasoning_budget": 4096,
  "chat_template_kwargs": {"enable_thinking": true},
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
