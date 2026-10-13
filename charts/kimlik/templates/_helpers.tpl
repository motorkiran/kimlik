{{- define "kimlik.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "kimlik.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{- define "kimlik.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{ include "kimlik.selectorLabels" . }}
app.kubernetes.io/version: {{ .Values.image.tag | default .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{- define "kimlik.selectorLabels" -}}
app.kubernetes.io/name: {{ include "kimlik.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{- define "kimlik.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "kimlik.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{- define "kimlik.secretName" -}}
{{- default (include "kimlik.fullname" .) .Values.secret.existingSecret }}
{{- end }}

{{- define "kimlik.image" -}}
{{- printf "%s:%s" .Values.image.repository (.Values.image.tag | default .Chart.AppVersion) }}
{{- end }}

{{/* Whether a migration Job prepares the database, so that the instances leave it alone. */}}
{{- define "kimlik.migrates" -}}
{{- if .Values.migration.job.enabled }}
{{- if not .Values.secret.existingSecret }}
{{- fail "migration.job.enabled needs secret.existingSecret: the Job runs before the release's own Secret exists." }}
{{- end }}
true
{{- end }}
{{- end }}

{{/*
The settings of a Kimlik container. `prepares` says whether the container prepares the database: the migration Job does,
and so does an instance when there is no Job. Only then does it get the provisioning document.
*/}}
{{- define "kimlik.env" -}}
{{- $secret := include "kimlik.secretName" .root -}}
- name: Kimlik__Server__PublicUrl
  value: {{ required "publicUrl is required: the address people reach Kimlik at, such as https://id.example.com/" .root.Values.publicUrl | quote }}
- name: ConnectionStrings__Kimlik
  valueFrom:
    secretKeyRef:
      name: {{ $secret }}
      key: {{ .root.Values.secret.keys.connectionString }}
- name: Kimlik__Security__MasterKey
  valueFrom:
    secretKeyRef:
      name: {{ $secret }}
      key: {{ .root.Values.secret.keys.masterKey }}
- name: Kimlik__Email__Smtp__Password
  valueFrom:
    secretKeyRef:
      name: {{ $secret }}
      key: {{ .root.Values.secret.keys.smtpPassword }}
      optional: true
- name: Kimlik__Bootstrap__AdminPassword
  valueFrom:
    secretKeyRef:
      name: {{ $secret }}
      key: {{ .root.Values.secret.keys.bootstrapAdminPassword }}
      optional: true
- name: Kimlik__Database__MigrateOnStartup
  value: {{ and .prepares (not .job) | quote }}
- name: Kimlik__Admin__Enabled
  value: {{ .root.Values.admin.enabled | quote }}
{{- if .root.Values.forwardedHeaders }}
- name: ASPNETCORE_FORWARDEDHEADERS_ENABLED
  value: "true"
{{- end }}
{{- if and .prepares .root.Values.provisioning }}
- name: Kimlik__Provisioning__FilePath
  value: /etc/kimlik/provisioning.json
{{- end }}
{{- range $name, $value := .root.Values.settings }}
- name: {{ $name }}
  value: {{ $value | quote }}
{{- end }}
{{- with .root.Values.extraEnv }}
{{ toYaml . }}
{{- end }}
{{- end }}

{{- define "kimlik.volumeMounts" -}}
- name: tmp
  mountPath: /tmp
{{- if and .prepares .root.Values.provisioning }}
- name: provisioning
  mountPath: /etc/kimlik
  readOnly: true
{{- end }}
{{- end }}

{{- define "kimlik.volumes" -}}
- name: tmp
  emptyDir: {}
{{- if and .prepares .root.Values.provisioning }}
- name: provisioning
  configMap:
    name: {{ include "kimlik.fullname" .root }}-provisioning
{{- end }}
{{- end }}
