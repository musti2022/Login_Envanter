#!/usr/bin/env bash
# Builds a throwaway Samba Active Directory domain controller with LDAPS for the AD integration
# tests. It is a test directory only: it never touches a real domain, and every password and key it
# creates is random and stays outside the repository.
#
#   sudo scripts/test-ad/setup-samba-ad.sh [target-dir]      # provision (first run) and start
#   sudo scripts/test-ad/setup-samba-ad.sh [target-dir] stop  # stop the server
#
# Requirements: Ubuntu/Debian with the samba-ad-dc, samba, ldb-tools, ldap-utils and openssl packages; root.
# Output: <target-dir>/test-ad.env (mode 600, owned by the user who ran sudo) with the EI_TEST_AD_* variables
# the tests read, so the tests themselves run without root.
# Domain: envanter.test (".test" is reserved for testing, RFC 2606), DC host dc1.envanter.test.
set -euo pipefail

TARGET_DIR="${1:-/opt/ei-test-ad}"
ACTION="${2:-start}"
REALM="ENVANTER.TEST"
NETBIOS_DOMAIN="ENVANTER"
DNS_DOMAIN="envanter.test"
HOST_NAME="dc1"
SERVER_FQDN="${HOST_NAME}.${DNS_DOMAIN}"
BASE_DN="DC=envanter,DC=test"
CONF="${TARGET_DIR}/etc/smb.conf"
TLS_DIR="${TARGET_DIR}/tls"
ENV_FILE="${TARGET_DIR}/test-ad.env"

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Run as root (Samba binds to port 636 and needs root to provision)." >&2
  exit 1
fi

for tool in samba samba-tool ldbadd ldbmodify ldbsearch ldapsearch openssl; do
  command -v "$tool" >/dev/null || {
    echo "Missing '$tool'. Install: apt-get install samba-ad-dc samba ldb-tools ldap-utils openssl" >&2
    exit 1
  }
done

# samba-tool's Python modules are built for the distribution's Python; when another python3 is the
# default, find the interpreter that can import them.
SAMBA_PYTHON=""
for candidate in python3 /usr/bin/python3.*; do
  [[ "$candidate" == *-config ]] && continue
  if command -v "$candidate" >/dev/null && "$candidate" -c "import samba" 2>/dev/null; then
    SAMBA_PYTHON="$candidate"
    break
  fi
done
if [[ -z "$SAMBA_PYTHON" ]]; then
  echo "No Python interpreter can import the samba module." >&2
  exit 1
fi
samba_tool() { "$SAMBA_PYTHON" "$(command -v samba-tool)" "$@"; }

stop_server() {
  if [[ -f "${TARGET_DIR}/run/samba.pid" ]]; then
    kill "$(cat "${TARGET_DIR}/run/samba.pid")" 2>/dev/null || true
  fi
  pkill -f "samba -s ${CONF}" 2>/dev/null || true
}

if [[ "$ACTION" == "stop" ]]; then
  stop_server
  echo "Stopped."
  exit 0
fi

wait_for_ldaps() {
  for _ in $(seq 1 60); do
    if LDAPTLS_CACERT="${TLS_DIR}/ca.crt" ldapsearch -LLL -x -H "ldaps://${SERVER_FQDN}" -s base -b "" \
      defaultNamingContext >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  echo "LDAPS on ${SERVER_FQDN}:636 did not come up; see ${TARGET_DIR}/log." >&2
  return 1
}

start_server() {
  mkdir -p "${TARGET_DIR}/run" "${TARGET_DIR}/log"
  if ! pgrep -f "samba -s ${CONF}" >/dev/null; then
    samba -s "$CONF" -D
  fi
  wait_for_ldaps
}

if ! grep -qE "[[:space:]]${SERVER_FQDN}([[:space:]]|$)" /etc/hosts; then
  echo "127.0.0.1 ${SERVER_FQDN} ${HOST_NAME}" >> /etc/hosts
fi

if [[ -f "$ENV_FILE" ]]; then
  start_server
  echo "Test AD is running. Variables: ${ENV_FILE}"
  exit 0
fi

random_password() {
  # Meets the AD complexity rules: upper, lower, digit and a symbol, 24+ characters.
  echo "Aa1!$(openssl rand -base64 24 | tr -d '/+=' | cut -c1-24)"
}

ADMIN_PASSWORD="$(random_password)"
USER_PASSWORD="$(random_password)"
SERVICE_PASSWORD="$(random_password)"

# Only ever wipe a directory this script created before (or an empty one).
if [[ "$TARGET_DIR" != /?* || "$TARGET_DIR" == "/" ]]; then
  echo "Target directory must be an absolute path other than /." >&2
  exit 1
fi
if [[ -e "$TARGET_DIR" && ! -d "$TARGET_DIR" ]]; then
  echo "${TARGET_DIR} exists and is not a directory; refusing to delete it." >&2
  exit 1
fi
if [[ -d "$TARGET_DIR" && -n "$(ls -A "$TARGET_DIR")" && ! -f "${TARGET_DIR}/private/sam.ldb" ]]; then
  echo "${TARGET_DIR} is not empty and is not a test AD directory; refusing to delete it." >&2
  exit 1
fi

stop_server
rm -rf "$TARGET_DIR"
mkdir -p "$TARGET_DIR" "$TLS_DIR"
# Readable so the tests can load the CA certificate and the variables file; the domain database
# (private/) and the DC key stay root-only.
chmod 755 "$TARGET_DIR" "$TLS_DIR"

samba_tool domain provision \
  --realm="$REALM" --domain="$NETBIOS_DOMAIN" --server-role=dc --dns-backend=NONE \
  --host-name="$HOST_NAME" --targetdir="$TARGET_DIR" --adminpass="$ADMIN_PASSWORD" \
  --option="log file = ${TARGET_DIR}/log/samba.log" \
  --option="pid directory = ${TARGET_DIR}/run" \
  >"${TARGET_DIR}/provision.log" 2>&1

# Test CA and DC certificate. The CA key is deleted once the server certificate is signed, so
# nothing else can ever be issued under this CA.
openssl req -x509 -newkey rsa:3072 -sha256 -days 825 -nodes \
  -keyout "${TLS_DIR}/ca.key" -out "${TLS_DIR}/ca.crt" \
  -subj "/O=EnterpriseInventory Test/CN=EnterpriseInventory Test AD CA" \
  -addext "basicConstraints=critical,CA:TRUE,pathlen:0" \
  -addext "keyUsage=critical,keyCertSign,cRLSign" 2>/dev/null
openssl req -newkey rsa:3072 -sha256 -nodes \
  -keyout "${TLS_DIR}/dc.key" -out "${TLS_DIR}/dc.csr" \
  -subj "/CN=${SERVER_FQDN}" 2>/dev/null
openssl x509 -req -in "${TLS_DIR}/dc.csr" -CA "${TLS_DIR}/ca.crt" -CAkey "${TLS_DIR}/ca.key" \
  -CAcreateserial -sha256 -days 397 -out "${TLS_DIR}/dc.crt" \
  -extfile <(printf '%s\n' \
    "basicConstraints=critical,CA:FALSE" \
    "keyUsage=critical,digitalSignature,keyEncipherment" \
    "extendedKeyUsage=serverAuth" \
    "subjectAltName=DNS:${SERVER_FQDN}") 2>/dev/null
rm -f "${TLS_DIR}/ca.key" "${TLS_DIR}/dc.csr" "${TLS_DIR}/ca.srl"
chmod 600 "${TLS_DIR}/dc.key"
chmod 644 "${TLS_DIR}/ca.crt" "${TLS_DIR}/dc.crt"

# Samba listens for LDAPS with this certificate on loopback only; simple binds are accepted only over
# TLS. Only the LDAP and KDC services run: the test needs nothing else.
sed -i '/^[[:space:]]*server services[[:space:]]*=/d' "$CONF"
sed -i "/^\[global\]/a\\
\tinterfaces = 127.0.0.1\\
\tbind interfaces only = yes\\
\ttls enabled = yes\\
\ttls keyfile = ${TLS_DIR}/dc.key\\
\ttls certfile = ${TLS_DIR}/dc.crt\\
\ttls cafile = ${TLS_DIR}/ca.crt\\
\tldap server require strong auth = yes\\
\tserver services = ldap, kdc" "$CONF"

SAM="${TARGET_DIR}/private/sam.ldb"
USERS_DN="CN=Users,${BASE_DN}"

samba_tool group add Bim_Envanter -H "$SAM" --description="EnterpriseInventory admins" >/dev/null
samba_tool group add Envanter_Ekibi -H "$SAM" --description="Nested in Bim_Envanter" >/dev/null
samba_tool group addmembers Bim_Envanter Envanter_Ekibi -H "$SAM" >/dev/null

# A decoy group that carries the same common name in another OU, so tests can prove access is
# decided by the configured group SID and never by a group name.
samba_tool ou add "OU=Sahte" -H "$SAM" >/dev/null
ldbadd -H "$SAM" >/dev/null <<LDIF
dn: CN=Bim_Envanter,OU=Sahte,${BASE_DN}
objectClass: group
sAMAccountName: Bim_Envanter_Sahte
description: Decoy with the same name, different SID
LDIF

add_user() {
  local sam="$1" given="$2" surname="$3"
  samba_tool user create "$sam" "$USER_PASSWORD" -H "$SAM" --use-username-as-cn \
    --given-name="$given" --surname="$surname" --mail-address="${sam}@${DNS_DOMAIN}" >/dev/null
}

add_user ayse.admin "Ayşe" "Yılmaz"          # direct member
add_user mehmet.user "Mehmet" "Öztürk"       # not a member
add_user nested.user "Nested" "Kullanıcı"    # member through Envanter_Ekibi only
add_user disabled.user "Pasif" "Kullanıcı"   # direct member, disabled
add_user expired.user "Süresi" "Dolmuş"      # direct member, account expired
add_user mustchange.user "Parola" "Değişecek" # direct member, must change password
add_user primary.user "Birincil" "Grup"      # Bim_Envanter is the primary group
add_user decoy.user "Sahte" "Grup"           # member of the decoy group only
add_user clash.member "Çakışan" "Üye"        # direct member whose logon name another account's UPN claims
add_user clash.other "Çakışan" "Diğer"       # not a member; its userPrincipalName is clash.member@<domain>
samba_tool user create svc.envanter "$SERVICE_PASSWORD" -H "$SAM" --use-username-as-cn \
  --description="EnterpriseInventory directory reader" >/dev/null

samba_tool group addmembers Bim_Envanter \
  ayse.admin,disabled.user,expired.user,mustchange.user,primary.user,clash.member -H "$SAM" >/dev/null
samba_tool group addmembers Envanter_Ekibi nested.user -H "$SAM" >/dev/null
ldbmodify -H "$SAM" >/dev/null <<LDIF
dn: CN=Bim_Envanter,OU=Sahte,${BASE_DN}
changetype: modify
add: member
member: CN=decoy.user,${USERS_DN}
LDIF

samba_tool user disable disabled.user -H "$SAM" >/dev/null
# accountExpires in the past (Windows FILETIME of 2020-01-01).
ldbmodify -H "$SAM" >/dev/null <<LDIF
dn: CN=expired.user,${USERS_DN}
changetype: modify
replace: accountExpires
accountExpires: 132223104000000000
LDIF
ldbmodify -H "$SAM" >/dev/null <<LDIF
dn: CN=mustchange.user,${USERS_DN}
changetype: modify
replace: pwdLastSet
pwdLastSet: 0
LDIF

# A bind as clash.member@<domain> must not open clash.other: AD resolves an explicit userPrincipalName before the
# implicit sAMAccountName@domain. Samba refuses to create such a clash through its own checks, so the UPN is written
# straight into the domain partition while the server is stopped.
ldbmodify -H "$SAM" >/dev/null <<LDIF
dn: CN=clash.member,${USERS_DN}
changetype: modify
delete: userPrincipalName
LDIF
ldbmodify -H "tdb://${TARGET_DIR}/private/sam.ldb.d/${BASE_DN^^}.ldb" >/dev/null <<LDIF
dn: CN=clash.other,${USERS_DN}
changetype: modify
replace: userPrincipalName
userPrincipalName: clash.member@${DNS_DOMAIN}
LDIF

group_sid() {
  ldbsearch -H "$SAM" -b "$1" -s base objectSid 2>/dev/null | awk '/^objectSid:/ {print $2}'
}
GROUP_SID="$(group_sid "CN=Bim_Envanter,${USERS_DN}")"
DECOY_GROUP_SID="$(group_sid "CN=Bim_Envanter,OU=Sahte,${BASE_DN}")"
GROUP_RID="${GROUP_SID##*-}"
ldbmodify -H "$SAM" >/dev/null <<LDIF
dn: CN=primary.user,${USERS_DN}
changetype: modify
replace: primaryGroupID
primaryGroupID: ${GROUP_RID}
LDIF

umask 077
cat >"$ENV_FILE" <<ENV
# Generated by scripts/test-ad/setup-samba-ad.sh. Test directory only; do not commit.
export EI_TEST_AD_SERVER=${SERVER_FQDN}
export EI_TEST_AD_PORT=636
export EI_TEST_AD_DOMAIN=${DNS_DOMAIN}
export EI_TEST_AD_BASE_DN='${BASE_DN}'
export EI_TEST_AD_CA_CERT=${TLS_DIR}/ca.crt
export EI_TEST_AD_GROUP_SID=${GROUP_SID}
export EI_TEST_AD_DECOY_GROUP_SID=${DECOY_GROUP_SID}
export EI_TEST_AD_USER_PASSWORD='${USER_PASSWORD}'
export EI_TEST_AD_SERVICE_USER=svc.envanter
export EI_TEST_AD_SERVICE_PASSWORD='${SERVICE_PASSWORD}'
ENV
if [[ -n "${SUDO_USER:-}" ]]; then
  chown "${SUDO_USER}" "$ENV_FILE"
fi

start_server
echo "Test AD is running at ldaps://${SERVER_FQDN}:636. Variables: ${ENV_FILE}"
