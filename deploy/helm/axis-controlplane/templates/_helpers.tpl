{{/* The image of the control plane. The tag defaults to the chart's appVersion. Call with the root context. */}}
{{- define "axis-controlplane.image" -}}
{{- $registry := .Values.image.registry | trimSuffix "/" -}}
{{- $tag := .Values.image.tag | default .Chart.AppVersion -}}
{{- if $registry -}}
{{ $registry }}/{{ .Values.image.repository }}:{{ $tag }}
{{- else -}}
{{ .Values.image.repository }}:{{ $tag }}
{{- end -}}
{{- end }}

{{/* Labels of every object. Call with the root context. */}}
{{- define "axis-controlplane.labels" -}}
app.kubernetes.io/name: axis-controlplane
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/* Labels that select the pods of the control plane. Call with the root context. */}}
{{- define "axis-controlplane.selectorLabels" -}}
app.kubernetes.io/name: axis-controlplane
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/component: controlplane
{{- end }}
