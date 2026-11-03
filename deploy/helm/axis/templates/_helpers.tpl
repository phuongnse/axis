{{/*
The image of a host. Call with a dict of "root" (the root context) and "repository".
The tag defaults to the chart's appVersion.
*/}}
{{- define "axis.image" -}}
{{- $registry := .root.Values.image.registry | trimSuffix "/" -}}
{{- $tag := .root.Values.image.tag | default .root.Chart.AppVersion -}}
{{- if $registry -}}
{{ $registry }}/{{ .repository }}:{{ $tag }}
{{- else -}}
{{ .repository }}:{{ $tag }}
{{- end -}}
{{- end }}

{{/* Labels of every object. Call with the root context. */}}
{{- define "axis.labels" -}}
app.kubernetes.io/name: axis
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/* Labels that select the pods of one component. Call with a dict of "root" and "component". */}}
{{- define "axis.selectorLabels" -}}
app.kubernetes.io/name: axis
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/component: {{ .component }}
{{- end }}

{{/* The pod security context. The images run as a non-root user. */}}
{{- define "axis.podSecurityContext" -}}
runAsNonRoot: true
seccompProfile:
  type: RuntimeDefault
{{- end }}

{{/* The container security context. The images work with a read-only root file system. */}}
{{- define "axis.containerSecurityContext" -}}
allowPrivilegeEscalation: false
readOnlyRootFilesystem: true
capabilities:
  drop:
    - ALL
{{- end }}

{{/*
The volume of /run/secrets. It projects the Kubernetes Secret keys of the named entries of
.Values.secrets into files named after the setting, as the images read them. Call with a
dict of "root" and "entries", the list of entry names the host needs.
*/}}
{{- define "axis.secretsVolume" -}}
{{- $files := dict "platformConnection" "ConnectionStrings__Platform" "platformSchemaConnection" "ConnectionStrings__PlatformSchema" "platformSecretKey" "Platform__SecretKey" "scenarioServerConnection" "Scenarios__ServerConnectionString" "identityDatabaseUrl" "KC_DB_URL" "identityDatabaseUsername" "KC_DB_USERNAME" "identityDatabasePassword" "KC_DB_PASSWORD" "identityAdminClientSecret" "IdentityProvider__AdminClientSecret" "backupStorageAccessKeyId" "Cell__BackupStorage__AccessKeyId" "backupStorageSecretAccessKey" "Cell__BackupStorage__SecretAccessKey" -}}
{{- $descriptions := dict "platformConnection" "the Kubernetes Secret that holds the platform connection string" "platformSchemaConnection" "the Kubernetes Secret that holds the platform schema connection string" "platformSecretKey" "the Kubernetes Secret that holds the installation key" "scenarioServerConnection" "the Kubernetes Secret that holds the scenario connection string" "identityDatabaseUrl" "the Kubernetes Secret that holds the JDBC URL of the identity provider's database" "identityDatabaseUsername" "the Kubernetes Secret that holds the identity provider's database user name" "identityDatabasePassword" "the Kubernetes Secret that holds the identity provider's database password" "identityAdminClientSecret" "the Kubernetes Secret that holds the axis-admin client secret" "backupStorageAccessKeyId" "the Kubernetes Secret that holds the access key id of the backup storage" "backupStorageSecretAccessKey" "the Kubernetes Secret that holds the secret access key of the backup storage" -}}
name: secrets
projected:
  # Readable by the non-root user of the image.
  defaultMode: 292
  sources:
    {{- range $entry := .entries }}
    {{- $reference := index $.root.Values.secrets $entry }}
    {{- $file := index $files $entry }}
    - secret:
        name: {{ required (printf "secrets.%s.name is required: %s (%s)" $entry (index $descriptions $entry) $file) $reference.name | quote }}
        items:
          - key: {{ $reference.key | default $file | quote }}
            path: {{ $file }}
    {{- end }}
{{- end }}

{{/* The mount of /run/secrets, read-only. */}}
{{- define "axis.secretsVolumeMount" -}}
name: secrets
mountPath: /run/secrets
readOnly: true
{{- end }}

{{/* The volumes of /tmp, extension packages and modules. Call with a dict of "root" and "entries". */}}
{{- define "axis.volumes" -}}
- {{ include "axis.secretsVolume" (dict "root" .root "entries" .entries) | nindent 2 | trim }}
- name: tmp
  emptyDir: {}
{{- with .root.Values.extensions.volume }}
- name: extensions
  {{- toYaml . | nindent 2 }}
{{- end }}
{{- with .root.Values.modules.volume }}
- name: modules
  {{- toYaml . | nindent 2 }}
{{- end }}
{{- end }}

{{/* The volume mounts of a host. Call with the root context. */}}
{{- define "axis.volumeMounts" -}}
- {{ include "axis.secretsVolumeMount" . | nindent 2 | trim }}
- name: tmp
  mountPath: /tmp
{{- if .Values.extensions.volume }}
- name: extensions
  mountPath: /extensions
  readOnly: true
{{- end }}
{{- if .Values.modules.volume }}
- name: modules
  mountPath: /modules
  readOnly: true
{{- end }}
{{- end }}

{{/* The environment settings that server and worker share. Call with the root context. */}}
{{- define "axis.hostEnv" -}}
- name: Tenancy__Source
  value: directory
- name: Extensions__Path
  value: /extensions
- name: Modules__Path
  value: /modules
{{- with .Values.tenancy.refreshInterval }}
- name: Tenancy__RefreshInterval
  value: {{ . | quote }}
{{- end }}
{{- with .Values.cell.id }}
- name: Cell__Id
  value: {{ . | quote }}
{{- end }}
{{- with .Values.cell.region }}
- name: Cell__Region
  value: {{ . | quote }}
{{- end }}
{{- with .Values.cell.tenantDomain }}
- name: Cell__TenantDomain
  value: {{ . | quote }}
{{- end }}
{{- with .Values.cell.internalDomain }}
- name: Cell__InternalDomain
  value: {{ . | quote }}
{{- end }}
{{- with .Values.cell.tenantCap }}
- name: Cell__TenantCap
  value: {{ . | quote }}
{{- end }}
{{- with .Values.cell.consoleHost }}
- name: Cell__ConsoleHost
  value: {{ . | quote }}
{{- end }}
{{- with .Values.cell.internalUrl }}
- name: Cell__InternalUrl
  value: {{ . | quote }}
{{- end }}
{{- with .Values.cell.operatorRealm }}
- name: Cell__OperatorRealm
  value: {{ . | quote }}
{{- end }}
{{- with .Values.cell.operatorRole }}
- name: Cell__OperatorRole
  value: {{ . | quote }}
{{- end }}
{{- with .Values.cell.backupKeyFile }}
- name: Cell__BackupKeyFile
  value: {{ . | quote }}
{{- end }}
{{- range $i, $network := .Values.cell.consoleAllowedNetworks }}
- name: Cell__ConsoleAllowedNetworks__{{ $i }}
  value: {{ $network | quote }}
{{- end }}
{{- end }}

{{/*
The settings that send the runtime connections to PgBouncer. Server and worker only: the publisher
connects to PostgreSQL directly. Renders nothing when PgBouncer is off. Call with the root context.
*/}}
{{- define "axis.pgbouncerEnv" -}}
{{- if .Values.cell.pgbouncer.enabled -}}
- name: Cell__RuntimeDatabaseHost
  value: {{ printf "%s-pgbouncer" .Release.Name | quote }}
- name: Cell__RuntimeDatabasePort
  value: "6432"
{{- end }}
{{- end }}

{{/* The probes of a host. Call with the name of the container port. A port named https is probed over HTTPS. */}}
{{- define "axis.probes" -}}
startupProbe:
  httpGet:
    path: /health/live
    port: {{ . }}
    {{- if eq . "https" }}
    scheme: HTTPS
    {{- end }}
  periodSeconds: 5
  failureThreshold: 30
livenessProbe:
  httpGet:
    path: /health/live
    port: {{ . }}
    {{- if eq . "https" }}
    scheme: HTTPS
    {{- end }}
  periodSeconds: 10
readinessProbe:
  httpGet:
    path: /health/ready
    port: {{ . }}
    {{- if eq . "https" }}
    scheme: HTTPS
    {{- end }}
  periodSeconds: 10
{{- end }}
