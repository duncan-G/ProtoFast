# Persistent Postgres data volume (D2). Holds BOTH Keycloak's `keycloak` DB and
# auth's durable `auth` DB. Its lifecycle is decoupled from Host B: it
# survives `terraform destroy/apply` of the instance (prevent_destroy), so
# Keycloak realms + auth data outlive instance replacement. Host B's
# user_data mounts it without reformatting an existing filesystem (§4.2); a
# graceful drain (deploy.sh drain, §6.2) unmounts it cleanly on every shutdown.

# AZ is pinned to the SUBNET, not the instance. host_b always launches into this
# one default subnet (a single fixed AZ), so the volume is necessarily co-located.
# Referencing aws_instance.host_b.availability_zone instead would make the volume's
# AZ "(known after apply)" whenever host_b is replaced (e.g. AL2023 AMI drift) —
# AZ is ForceNew, so Terraform would then plan to DESTROY+recreate the volume and
# trip prevent_destroy. Pinning to the subnet's known-at-plan-time AZ keeps the
# volume put and lets Terraform merely detach + reattach on instance replacement
# (same cycle-breaking trick as the static private IPs in compute.tf).
resource "aws_ebs_volume" "pgdata" {
  availability_zone = aws_default_subnet.default.availability_zone
  size              = var.pgdata_volume_gb
  type              = "gp3"
  encrypted         = true
  tags              = { Name = "${var.project}-pgdata" }

  # Outlives instance teardown. Removing the volume is a deliberate, manual act.
  lifecycle {
    prevent_destroy = true
  }
}

resource "aws_volume_attachment" "pgdata" {
  device_name = "/dev/sdf"
  volume_id   = aws_ebs_volume.pgdata.id
  instance_id = aws_instance.host_b.id

  # On instance replacement, detach cleanly rather than force-detaching a mounted
  # volume. The graceful drain (§6.2) unmounts first; this is the Terraform-side
  # safety net.
  stop_instance_before_detaching = true
}
