#!/usr/bin/env bash
# Makes the self-signed certificate the phone remote serves https with.
#   usage: make-cert.sh <output .bytes/.pfx path>
# Browsers only allow the microphone on https pages; guests tap past the "not private" warning.
# Apple's rules for TLS certificates (SAN, serverAuth, <= 825 days, RSA 2048, SHA-256) are followed
# so Safari on iPhone lets people continue. PKCS#12 uses SHA1/3DES because Unity's Mono can't read
# OpenSSL 3's default AES/PBKDF2 encryption.
set -euo pipefail
out="$1"
work="$(mktemp -d)"
mkdir -p "$(dirname "$out")"
openssl req -x509 -newkey rsa:2048 -sha256 -days 820 -nodes \
  -keyout "$work/key.pem" -out "$work/cert.pem" \
  -subj "/CN=YARG Resurrected phone remote" \
  -addext "subjectAltName=DNS:yarg.local" \
  -addext "keyUsage=critical,digitalSignature,keyEncipherment" \
  -addext "extendedKeyUsage=serverAuth" 2>/dev/null
openssl pkcs12 -export -inkey "$work/key.pem" -in "$work/cert.pem" -out "$out" \
  -passout pass:yarg -name "YARG Remote" \
  -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES -macalg sha1
rm -rf "$work"
echo "Wrote $out"
