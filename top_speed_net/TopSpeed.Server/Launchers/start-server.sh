#!/bin/sh
# Starts the TopSpeed server kept in this folder.
cd "$(dirname "$0")" || exit 1
exec ./TopSpeed.Server
