#!/bin/sh
#


set -e

if [ "${1#-}" != "${1}" ] || [ -z "$(command -v "${1}")" ]; then
  set -- ./artefacts/fluxzy "$@"
fi

exec "$@"
