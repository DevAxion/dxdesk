#!/usr/bin/env bash
# DXDesk relay üçün self-signed TLS sertifikatı yaradır (5 il).
#
# İstifadə (deploy/relay qovluğunda):
#   ./make-cert.sh 10.30.2.11            # relay-in IP-si (və ya DNS adı)
#
# Nəticə:
#   certs/relay.pfx          — relay-in sertifikatı (gizli açar daxil, kənara verməyin)
#   .env-ə əlavə ediləcək sətirlər və kompüterlərə yazılacaq barmaq izi ekrana çıxır.
#
# Kompüterlər sertifikatı barmaq izi ilə tanıyır (Relay:CertThumbprint), ona görə
# sertifikatı GPO ilə yaymağa ehtiyac yoxdur.
set -euo pipefail

HOST="${1:?Relay-in IP-sini və ya adını verin, məs: ./make-cert.sh 10.30.2.11}"
mkdir -p certs
chmod 700 certs

if [[ "$HOST" =~ ^[0-9.]+$ ]]; then SAN="IP:$HOST"; else SAN="DNS:$HOST"; fi
PASS="$(openssl rand -hex 24)"

openssl req -x509 -newkey rsa:3072 -sha256 -days 1825 -nodes \
  -subj "/CN=DXDesk Relay" -addext "subjectAltName=$SAN" \
  -keyout certs/relay.key -out certs/relay.crt 2>/dev/null
openssl pkcs12 -export -out certs/relay.pfx -inkey certs/relay.key -in certs/relay.crt \
  -passout "pass:$PASS"
rm -f certs/relay.key
# Konteynerdəki "app" istifadəçisi (uid 1654) oxuya bilsin.
chown -R 1654:1654 certs 2>/dev/null || true
chmod 600 certs/relay.pfx

FP="$(openssl x509 -in certs/relay.crt -noout -fingerprint -sha256 | cut -d= -f2 | tr -d ':')"

cat <<EOF

=== Sertifikat hazırdır ===

1) Bu sətirləri deploy/relay/.env faylına əlavə edin:

RELAY_URLS=https://0.0.0.0:5100
RELAY_CERT_PATH=/app/certs/relay.pfx
RELAY_CERT_PASSWORD=$PASS

2) Relay-i yenidən qaldırın:
   sudo docker compose up -d --build

3) Bütün appsettings.json fayllarında (Service paketi + admin Server):
   "Url": "https://$HOST:5100/remotehub",
   "CertThumbprint": "$FP"

EOF
