# Persistent Postgres data volume (D2). Holds BOTH Keycloak's `keycloak` DB and
# auth's durable `auth` DB. Its lifecycle is decoupled from Host B: it
# survives `terraform destroy/apply` of the instance (prevent_destroy), so
# Keycloak realms + auth data outlive instance replacement. Host B's
# user_data mounts it without reformatting an existing filesystem (§4.2); a
# graceful drain (deploy.sh drain, §6.2) unmounts it cleanly on every shutdown.

resource "aws_ebs_volume" "pgdata" {
  availability_zone = aws_instance.host_b.availability_zone
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
