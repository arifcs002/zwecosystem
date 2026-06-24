#!/bin/bash
if [ -z "$1" ]; then
    echo "Usage: ./update_appsettings.sh <new_db_password>"
    exit 1
fi

DB_PASSWORD=$1

if [ ! -f "appsettings.example.json" ]; then
    echo "Error: appsettings.example.json not found."
    exit 1
fi

sed "s/YOUR_DB_PASSWORD/$DB_PASSWORD/g" appsettings.example.json | sed "s/YOUR_DB_HOST/194.5.152.74/g" | sed "s/YOUR_DB_NAME/ecommerce_db/g" | sed "s/YOUR_DB_USER/dev_user/g" > appsettings.json

echo "appsettings.json has been securely updated!"
