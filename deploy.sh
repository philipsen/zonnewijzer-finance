#!/bin/bash
scp docker-compose.production.yml 10.0.0.1:compose/zonnewijzer-finance/
ssh 10.0.0.1 "cd compose/zonnewijzer-finance; ./update-and-restart.sh"